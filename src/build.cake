//Don't define #tool here. Just add there to 'paket.dependencies' 'build' group
//Don't use #addin here. Use #r to load the dll found in the nuget package.
#r "./packages/build/Octokit/lib/net46/Octokit.dll"
#r "./packages/build/Cake.Git/lib/net461/Cake.Git.dll"
#r "System.Net.Http.dll"

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////
readonly var Target = Argument("target", "Default");
readonly var Configuration = Argument("configuration", "Debug");
var runInDryRun = Argument<bool>("isDryRun", false);
readonly var GitHubOwner = Argument("gitHubOwner", "git-tfs");
readonly var GitHubRepository = Argument("gitHubRepository", "git-tfs");
readonly var IdGitHubReleaseToDelete = Argument<int>("idGitHubReleaseToDelete", -1);
readonly var IsMinorRelease = Argument<bool>("isMinorRelease", false);

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////
const string ApplicationName = "GitTfs";
const string ZipFileTemplate = ApplicationName + "-{0}.zip";
const string ApplicationPath = "./" + ApplicationName;
const string PathToSln = ApplicationPath + ".sln";
const string TargetFramework = "net48"; //due to new dotnet csproj format
readonly var OutDir = "bin/" + Configuration + "/" + TargetFramework + "/";
const string buildAssetPath = @".\.build\";
const string _downloadUrlBase = "https://github.com/lucianm/git-tfs";
const string DownloadUrlTemplate = _downloadUrlBase + "/releases/download/v{0}/";
// Fork-specific identifiers (empty for upstream)
const string _forkOwner = "lucianm"; // "git-tfs" for upstream
const string _forkPublisher = "LucianM"; // "GitTfs" for upstream  
const string _forkPackageSuffix = "Lfs"; // "" for upstream
string ReleaseNotesPath = @"..\doc\release-notes\NEXT.md";
const string ChocolateyBuildDir = buildAssetPath + "chocolatey";
readonly var OutputDirectory = ApplicationPath + "/" + OutDir;
const string TestProjectName = "GitTfsTest";

// Define directories.
readonly var buildDir = Directory(OutputDirectory);
string _semanticVersionShort = ""; //0.26.179
string _semanticVersionLong  = ""; //0.26.179+4890c16f54f1b354aa198773aa9530a04d575932.master
string _zipFilePath;
string _zipFilename;
string _downloadUrl;
string _releaseVersion;
string _sha1;
string _appVeyorBuildVersion;
bool _buildAllVersion = (Target == "AppVeyorRelease");
Cake.Common.Security.FileHash _sha256;
string _shaFilePath;
string _scoopManifestPath;
string _scoopManifestZip;
string _wingetVersionPath;
string _wingetInstallerPath;
string _wingetLocalePath;
string _wingetManifestZip;
string _chocolateyManifestZip;

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////
Task("Help").Description("This help...")
	.Does(() =>
{
	Information(
@"Trigger the release process to AppVeyor:
----------------------------------------
1. Setup the personal data in `PersonalTokens.config` file in the repo root folder.
2. From `src` folder, run `.\build.ps1 -Target ""TriggerRelease""`

Release process from local machine:
-----------------------------------
1. Setup the personal data in `PersonalTokens.config` file in the repo root folder.
2. From `src` folder, run `.\build.ps1 -Target ""Release"" -Configuration ""Release""`

Example with parameters:
------------------------
run `.\build.ps1 -Target ""DryRunRelease"" -isMinorRelease=true`

Available tasks:");
	StartProcess("cake.exe", "build.cake -showdescription");
});

Task("DryRun").Description("Set the dry-run flag")
	.Does(() =>
{
	Information("Doing a dry run!!!!");
	runInDryRun = true;
});

Task("TagVersion").Description("Validate release notes exist for this version")
	.Does(() =>
{
	var version = GitVersion();
	var expectedReleaseNotePath = @"..\doc\release-notes\v" + version.MajorMinorPatch + ".md";
	
	if(!FileExists(expectedReleaseNotePath))
	{
		throw new Exception(
			$"Release notes file not found: {expectedReleaseNotePath}\n" +
			"Please create the release notes file before tagging:\n" +
			"  1. Copy NEXT.md to v" + version.MajorMinorPatch + ".md\n" +
			"  2. Commit the new file\n" +
			"  3. Create and push the tag: git tag v" + version.MajorMinorPatch + " && git push --follow-tags"
		);
	}
	
	Information($"Found release notes: {expectedReleaseNotePath}");
});

Task("Clean").Description("Clean the working directory")
	.Does(() =>
{
	MSBuild(PathToSln, settings => {

		settings.SetConfiguration(Configuration)
			.SetVerbosity(Verbosity.Minimal)
			.WithTarget("Clean");
	});
});

Task("Restore-NuGet-Packages").Description("Restore nuget dependencies (with paket)")
	.Does(() =>
{
	StartProcess(FileExists("paket.exe") ? "paket.exe" : @".paket\paket.exe", "restore");
	StartProcess("dotnet", $"restore {PathToSln}");
});

Task("Version").Description("Get the version using GitVersion")
	.Does(() =>
{
	var version = GitVersion();
	// Use GitVersion's SemVer for better tagged commit handling
	_semanticVersionShort = version.MajorMinorPatch;
	_semanticVersionLong = version.InformationalVersion;
	Information("Semantic version (short):" + _semanticVersionShort);
	Information("Semantic version (long ):" + _semanticVersionLong);

	//Update all the variables now that we know the version number
	var normalizedBranchName = NormalizeBrancheName(version.BranchName);
	_sha1 = version.Sha;
	var shortSha1 = version.Sha.Substring(0,8);
	// For tagged releases (CommitsSinceVersionSource == 0) or master branch, use clean version without postfix
	var isTaggedRelease = version.CommitsSinceVersionSource == 0 || version.BranchName == "master" || version.BranchName.StartsWith("tags/");
	var postFix = isTaggedRelease ? string.Empty : "-" + shortSha1 + "." + normalizedBranchName;
	_zipFilename = string.Format(ZipFileTemplate, _semanticVersionShort + postFix);
	_zipFilePath = System.IO.Path.Combine(buildAssetPath, _zipFilename);
	_shaFilePath = _zipFilePath + ".sha256";
	_downloadUrl = string.Format(DownloadUrlTemplate, _semanticVersionShort) + _zipFilename;

	_releaseVersion = "v" + _semanticVersionShort;
	
	// Derive release notes path from version (tag-driven releases)
	ReleaseNotesPath = @"..\doc\release-notes\" + _releaseVersion + ".md";
	Information("Release notes path: " + ReleaseNotesPath);

	// Guard against non-AppVeyor environments
	var buildNumber = EnvironmentVariable("APPVEYOR_BUILD_NUMBER") ?? "0";
	_appVeyorBuildVersion = _semanticVersionShort
			+ ((version.BranchName == "master") ? string.Empty : "+" + shortSha1 + "." + normalizedBranchName)
			+ "." + buildNumber;
});

void UpdateAppVeyorBuildNumber()
{
	Information("Updating Appveyor version to... " + _appVeyorBuildVersion);
	AppVeyor.UpdateBuildVersion(_appVeyorBuildVersion);
}

string NormalizeBrancheName(string branchName)
{
	return branchName.Replace('/', '_').Replace('\\', '_');
}

Task("UpdateAssemblyInfo").Description("Update AssemblyInfo properties with the Git Version")
	.IsDependentOn("Version")
	.Does(() =>
{
	CreateAssemblyInfo("CommonAssemblyInfo.cs", new AssemblyInfoSettings {
		Company="GitTfs",
		Product = "GitTfs",
		Copyright = "Copyright © 2009-" + DateTime.Now.Year,
		Version = _semanticVersionShort,
		FileVersion = _semanticVersionShort,
		InformationalVersion = _semanticVersionLong
	});
});

Task("Build").Description("Build git-tfs")
	.IsDependentOn("Restore-NuGet-Packages")
	.IsDependentOn("UpdateAssemblyInfo")
	.Does(() =>
{
	// Use MSBuild
	// /logger:"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll" /nologo /p:BuildInParallel=true /m:4
	MSBuild(PathToSln, settings => {

		settings.SetConfiguration(Configuration)
			.SetVerbosity(Verbosity.Minimal)
			.SetMaxCpuCount(4)
			.UseToolVersion(MSBuildToolVersion.VS2022)
			;
		settings.WithTarget("GitTfs_Vs2019")
				.WithTarget("GitTfs_Vs2022")
				.WithTarget(TestProjectName);
	});
});

void SetGitUserConfig()
{
	if(!BuildSystem.IsLocalBuild)
	{
		Information("Setting git user config to run some integration tests...");
		//Merge with libgit2sharp now require having user name and email to be set!
		StartProcess("git.exe", "config --global user.name \"git-tfs user for merge in unit tests\"");
		StartProcess("git.exe", "config --global user.email \"git-tfs@unit-tests.com\"");
	}
}

Task("Run-Unit-Tests").Description("Run the unit tests")
	.IsDependentOn("Build")
	.Does(() =>
{
	SetGitUserConfig();

	EnsureDirectoryExists(buildAssetPath);
	var coverageFile = System.IO.Path.Combine(buildAssetPath, "coverage.xml");
	OpenCover(tool => {
		tool.XUnit2("./"+ TestProjectName + "/" + OutDir + TestProjectName +".dll", new XUnit2Settings()
		{
			XmlReport = true,
			OutputDirectory = ".",
			UseX86 =  true
		});
	},
	new FilePath(coverageFile),
	new OpenCoverSettings()
		{
			WorkingDirectory = MakeAbsolute(Directory("./"+ TestProjectName + "/" + OutDir)),
			Register = "user"
		}
		 .WithFilter("+[git-tfs*]*")
		 .WithFilter("-[LibGit2Sharp]*")
		);

	if(BuildSystem.IsRunningOnAppVeyor)
	{
		Information("Upload coverage to AppVeyor...");
		BuildSystem.AppVeyor.UploadArtifact(coverageFile);
	}
	if(BuildSystem.IsRunningOnAzurePipelinesHosted)
	{
		Information("Upload coverage to VSTS...");
		BuildSystem.AzurePipelines.Commands.UploadArtifact("reports", coverageFile, "coverage.xml");
	}

	var coverageResultFolder = System.IO.Path.Combine(buildAssetPath, "coverage");
	ReportGenerator(new FilePath(coverageFile), coverageResultFolder, new ReportGeneratorSettings(){
		ToolPath = @".\packages\build\ReportGenerator\tools\net47\ReportGenerator.exe"
	});
	if(!BuildSystem.IsLocalBuild)
	{
		var coverageZip = System.IO.Path.Combine(buildAssetPath, "coverage.zip");
		Zip(coverageResultFolder, coverageZip);
		if(BuildSystem.IsRunningOnAppVeyor)
		{
			Information("Upload coverage zipped to AppVeyor...");
			BuildSystem.AppVeyor.UploadArtifact(coverageZip);
		}
		if(BuildSystem.IsRunningOnAzurePipelinesHosted)
		{
			Information("Upload coverage zipped to VSTS...");
			BuildSystem.AzurePipelines.Commands.UploadArtifact("reports", coverageZip, "coverage.zip");
		}
	}
});

Task("Run-Smoke-Tests").Description("Run the functional/smoke tests")
	.IsDependentOn("Run-Unit-Tests")
	.Does(() =>
{
	var tmpDirectory = System.IO.Path.Combine(EnvironmentVariable("TMP"), "gittfs");
	EnsureDirectoryExists(tmpDirectory);
	CleanDirectory(tmpDirectory);

	var aboluteBuildDir = MakeAbsolute(Directory(buildDir));
	var absoluteSmokeTestsScript = MakeAbsolute(File(@".\build\FunctionalTesting\smoke_tests.ps1"));

	var exitCode = StartProcess("powershell.exe", new ProcessSettings
		{
			Arguments = "-file \""+ absoluteSmokeTestsScript +"\" -gittfsFolder \""+ aboluteBuildDir + "\"",
			WorkingDirectory = tmpDirectory
		});
	if(exitCode != 0)
	{
		throw new Exception("Fail to run the smoke tests");
	}
});

Task("Package").Description("Generate the release zip file")
	.IsDependentOn("Build")
	.Does(() =>
{
	CreateDirectory(ChocolateyBuildDir);

	//Prepare the zip
	var libgit2NativeBinariesFolder = OutputDirectory + @"\NativeBinaries";




	CopyDirectory(@"..\doc", OutputDirectory + @"\doc");

	CopyFiles(new[] {@"..\README.md", @"..\LICENSE", @"..\NOTICE"}, OutputDirectory);
	CopyFiles(new[] {@".\build\CorFlags.exe", @".\build\enable_checkin_policies_support.bat", @".\build\disable_checkin_policies_support.bat"}, OutputDirectory);
	
	// Build WinGet launcher for portable package distribution
	Information("Building WinGet launcher...");
	MSBuild(@".\build\WinGetLauncher.csproj", settings => {
		settings.SetConfiguration(Configuration)
			.SetVerbosity(Verbosity.Minimal)
			.UseToolVersion(MSBuildToolVersion.VS2022)
			.WithTarget("Build");
	});
	
	DeleteFiles(OutputDirectory + @"\**\*.pdb");

	//Create the zip
	Zip(OutputDirectory, _zipFilePath);

	// calculate sha256 hash, store in variable and file artifact, usable in Choco, Scoop, WinGet...
	_sha256 = CalculateFileHash(_zipFilePath);
	System.IO.File.WriteAllText(_shaFilePath, $"{_sha256.ToHex()}  {_zipFilename}");
	Information($"Hash ({_sha256.Algorithm:G}):" + _sha256.ToHex());

	if(!BuildSystem.IsLocalBuild)
	{
		if(BuildSystem.IsRunningOnAppVeyor)
		{
			Information("Upload artifacts to AppVeyor...");
			BuildSystem.AppVeyor.UploadArtifact(_zipFilePath);
			BuildSystem.AppVeyor.UploadArtifact(_shaFilePath);
		}
		if(BuildSystem.IsRunningOnAzurePipelinesHosted)
		{
			Information("Upload artifacts to VSTS...");
			BuildSystem.AzurePipelines.Commands.UploadArtifact("install", _zipFilePath, _zipFilename);
			BuildSystem.AzurePipelines.Commands.UploadArtifact("install", _shaFilePath, System.IO.Path.GetFileName(_shaFilePath));
		}
	}
});

void DisplayAuthTokenErrorMessage(string error)
{
	var errorMessage = @"Please create a file 'PersonalTokens.config' containing your authentication tokens
See the file 'PersonalTokens.config.example' for the format and content.
Error: " + error;

	throw new Exception(errorMessage);
}

string ReadToken(string tokenKey, string tokenRegexFormat = null)
{
	var authTargetsFile = @"..\PersonalTokens.config";

	Information($"Reading token '{tokenKey}'...");

	if(!FileExists(authTargetsFile))
		DisplayAuthTokenErrorMessage("File not found");

	var personalToken = System.IO.File.ReadAllLines(authTargetsFile).FirstOrDefault(l => l.StartsWith(tokenKey + "="));
	if(personalToken == null)
		DisplayAuthTokenErrorMessage("Key not found in file");

	personalToken = personalToken.Trim();
	personalToken = personalToken.Substring(tokenKey.Length+1, personalToken.Length-tokenKey.Length-1);
	if(tokenRegexFormat == null)
	{
		Information($"Value found!");
		return personalToken;
	}

	var regexToken = new System.Text.RegularExpressions.Regex(tokenRegexFormat);
	if(!regexToken.IsMatch(personalToken))
		DisplayAuthTokenErrorMessage($"Format of value found not valid: {tokenRegexFormat}" + (BuildSystem.IsLocalBuild ? $" / {personalToken}" : ""));
	return personalToken;
}

string GetChocolateyToken()
{
	var token = Argument("chocolateyToken", "");
	if(!string.IsNullOrEmpty(token))
	{
		Information("Chocolatey token found in arguments!");
		return token;
	}

	token = EnvironmentVariable("chocolateyToken");
	if(!string.IsNullOrEmpty(token))
	{
		Information("Chocolatey token found in env variables!");
		return token;
	}

	return ReadToken("Chocolatey");
}

string GetGithubUserAccount()
{
	var token = Argument("gitHubUserAccount", "");
	if(!string.IsNullOrEmpty(token))
	{
		Information("GitHub user account '" + token + "' found in script arguments!");
		return token;
	}

	token = EnvironmentVariable("gitHubUserAccount");
	if(!string.IsNullOrEmpty(token))
	{
		Information("GitHub user account '" + token + "' found in env variables!");
		return token;
	}

	return ReadToken("GitHubUserAccount");
}

string GetGithubAuthToken()
{
	var token = Argument("gitHubToken", "");
	if(!string.IsNullOrEmpty(token))
	{
		Information("GitHub token found in script arguments!");
		return token;
	}

	token = EnvironmentVariable("gitHubToken");
	if(!string.IsNullOrEmpty(token))
	{
		Information("GitHub token found in env variables!");
		return token;
	}

	return ReadToken("GitHub", @"^(ghp_|github_pat_).+");
}

string ReadReleaseNotes()
{
	if(!FileExists(ReleaseNotesPath))
	{
		Warning($"Release notes file not found: {ReleaseNotesPath}");
		return string.Empty;
	}
	var notes = System.IO.File.ReadAllText(ReleaseNotesPath);
	Information($"Loaded release notes from: {ReleaseNotesPath}");
	return notes;
}

void GenerateScoopManifest()
{
	Information("Generating Scoop manifest...");
	
	var packageSuffix = string.IsNullOrEmpty(_forkPackageSuffix) ? "" : "-" + _forkPackageSuffix.ToLower();
	var description = string.IsNullOrEmpty(_forkPackageSuffix)
		? "A Git/TFS bridge, similar to git-svn."
		: "A Git/TFS bridge with full Git LFS support, similar to git-svn.";
	
	// Build manifest matching upstream Scoop format
	var scoopManifest = new
	{
		version = _semanticVersionShort,
		description = description,
		homepage = _downloadUrlBase,
		license = "Apache-2.0",
		depends = "git",
		url = _downloadUrl,
		hash = _sha256.ToHex().ToLower(),
		bin = "git-tfs.exe",
		checkver = new
		{
			github = _downloadUrlBase
		},
		autoupdate = new
		{
			url = $"{_downloadUrlBase}/releases/download/v$version/GitTfs-$version.zip"
		}
	};
	
	var scoopDir = System.IO.Path.Combine(buildAssetPath, "scoop");
	EnsureDirectoryExists(scoopDir);
	CleanDirectory(scoopDir);
	
	_scoopManifestPath = System.IO.Path.Combine(scoopDir, $"git-tfs{packageSuffix}.json");
	var json = Newtonsoft.Json.JsonConvert.SerializeObject(scoopManifest, Newtonsoft.Json.Formatting.Indented);
	System.IO.File.WriteAllText(_scoopManifestPath, json + Environment.NewLine);
	Information($"Scoop manifest created: {_scoopManifestPath}");
	
	// Zip the Scoop manifest
	_scoopManifestZip = System.IO.Path.Combine(buildAssetPath, "manifest.scoop.zip");
	Zip(scoopDir, _scoopManifestZip);
	Information($"Scoop manifest zipped: {_scoopManifestZip}");
	
	if(BuildSystem.IsRunningOnAppVeyor)
	{
		Information("Uploading Scoop manifest as AppVeyor artifact...");
		BuildSystem.AppVeyor.UploadArtifact(_scoopManifestZip);
	}
	if(BuildSystem.IsRunningOnAzurePipelinesHosted)
	{
		Information("Uploading Scoop manifest as Azure Pipelines artifact...");
		BuildSystem.AzurePipelines.Commands.UploadArtifact("install", _scoopManifestZip, "manifest.scoop.zip");
	}
}

void GenerateWinGetManifest()
{
	Information("Generating WinGet manifest...");
	
	// WinGet uses YAML manifests with three files: version, installer, and locale
	// Format: manifests/<first-letter>/<Publisher>/<PackageName>/<Version>/
	var wingetBaseDir = System.IO.Path.Combine(buildAssetPath, "winget");
	var publisher = string.IsNullOrEmpty(_forkPublisher) ? "GitTfs" : _forkPublisher;
	var packageId = $"{publisher}.GitTfs{_forkPackageSuffix}";
	var packageName = string.IsNullOrEmpty(_forkPackageSuffix) ? "git-tfs" : $"git-tfs-{_forkPackageSuffix.ToLower()}";
	
	// Create versioned directory structure
	var wingetDir = System.IO.Path.Combine(wingetBaseDir, _semanticVersionShort);
	EnsureDirectoryExists(wingetDir);
	CleanDirectory(wingetDir);
	
	// Version manifest
	var versionManifest = $@"# Created using Cake Build
# yaml-language-server: $schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json

PackageIdentifier: {packageId}
PackageVersion: {_semanticVersionShort}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.12.0
";
	_wingetVersionPath = System.IO.Path.Combine(wingetDir, $"{packageId}.yaml");
	System.IO.File.WriteAllText(_wingetVersionPath, versionManifest);
	
	// Installer manifest
	var installerManifest = $@"# Created using Cake Build
# yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json

PackageIdentifier: {packageId}
PackageVersion: {_semanticVersionShort}
InstallerType: zip
Installers:
- Architecture: x64
  NestedInstallerType: portable
  NestedInstallerFiles:
  - RelativeFilePath: git-tfs-launcher.exe
    PortableCommandAlias: git-tfs
  InstallerUrl: {_downloadUrl}
  InstallerSha256: {_sha256.ToHex()}
  Dependencies:
    PackageDependencies:
    - PackageIdentifier: Git.Git
ManifestType: installer
ManifestVersion: 1.12.0
";
	_wingetInstallerPath = System.IO.Path.Combine(wingetDir, $"{packageId}.installer.yaml");
	System.IO.File.WriteAllText(_wingetInstallerPath, installerManifest);
	
	// Locale manifest
	var releaseNotes = ReadReleaseNotes();
	if(string.IsNullOrEmpty(releaseNotes))
	{
		releaseNotes = $"See {_downloadUrlBase}/releases/tag/v{_semanticVersionShort}";
	}
	// WinGet schema 1.12.0 allows up to 10000 characters for ReleaseNotes
	if(releaseNotes.Length > 10000)
	{
		releaseNotes = releaseNotes.Substring(0, 9997) + "...";
	}
	// Escape special characters for YAML string formatting
	releaseNotes = releaseNotes.Replace("\\", "\\\\").Replace("\"", "\\\"");
	
	var publisherName = string.IsNullOrEmpty(_forkPublisher) ? "git-tfs contributors" : "Lucian Muresan";
	var description = string.IsNullOrEmpty(_forkPackageSuffix) 
		? "git-tfs is a two-way bridge between TFS/Azure DevOps and Git, allowing you to work with a Git repository while interacting with TFS."
		: "git-tfs is a two-way bridge between TFS/Azure DevOps and Git, allowing you to work with a Git repository while interacting with TFS. This fork includes full Git LFS awareness via filter-process protocol, supporting LFS right from cloning from TFVC.";
	
	var localeManifest = $@"# Created using Cake Build
# yaml-language-server: $schema=https://aka.ms/winget-manifest.defaultLocale.1.12.0.schema.json

PackageIdentifier: {packageId}
PackageVersion: {_semanticVersionShort}
PackageLocale: en-US
Publisher: {publisherName}
PublisherUrl: {_downloadUrlBase}
PackageName: {packageName}
PackageUrl: {_downloadUrlBase}
License: Apache-2.0
LicenseUrl: {_downloadUrlBase}/blob/master/LICENSE
ShortDescription: A Git/TFS bridge with full Git LFS support
Description: {description}
Tags:
- git
- tfs
- version-control
- azure-devops
- lfs
- git-lfs
ReleaseNotes: ""{releaseNotes}""
ReleaseNotesUrl: {_downloadUrlBase}/releases/tag/v{_semanticVersionShort}
ManifestType: defaultLocale
ManifestVersion: 1.12.0
";
	_wingetLocalePath = System.IO.Path.Combine(wingetDir, $"{packageId}.locale.en-US.yaml");
	System.IO.File.WriteAllText(_wingetLocalePath, localeManifest);
	
	Information($"WinGet manifests created in: {wingetDir}");
	
	// Zip the WinGet manifests
	_wingetManifestZip = System.IO.Path.Combine(buildAssetPath, "manifest.winget.zip");
	Zip(wingetDir, _wingetManifestZip);
	Information($"WinGet manifests zipped: {_wingetManifestZip}");
	
	if(BuildSystem.IsRunningOnAppVeyor)
	{
		Information("Uploading WinGet manifest as AppVeyor artifact...");
		BuildSystem.AppVeyor.UploadArtifact(_wingetManifestZip);
	}
	if(BuildSystem.IsRunningOnAzurePipelinesHosted)
	{
		Information("Uploading WinGet manifest as Azure Pipelines artifact...");
		BuildSystem.AzurePipelines.Commands.UploadArtifact("install", _wingetManifestZip, "manifest.winget.zip");
	}
}

Octokit.GitHubClient GetGithubClient()
{
	var githubToken = GetGithubAuthToken();
	var client = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("git-tfs-releasing"));
	var tokenAuth = new Octokit.Credentials(githubToken);
	client.Credentials = tokenAuth;
	return client;
}

Task("TriggerRelease").Description("Trigger a release from the AppVeyor build server")
	.Does(() =>
{
	TriggerRelease(false);
});

Task("TriggerMinorRelease").Description("Trigger a minor release from the AppVeyor build server")
	.Does(() =>
{
	TriggerRelease(true);
});

void TriggerRelease(bool isMinorRelease)
{
	Information("gitHubUserAccount: "+ GetGithubUserAccount());
	var httpClient = new System.Net.Http.HttpClient();
	//AppVeyor build data to trigger the git-tfs build + parameters passed to the release build
	var content = @"{
accountName: 'pmiossec',
projectSlug: 'git-tfs-v2qcm',
branch: 'master',
environmentVariables: {
 target: 'AppVeyorRelease',
 chocolateyToken: '"+ GetChocolateyToken() + @"',
 gitHubUserAccount: '"+ GetGithubUserAccount() + @"',
 gitHubToken: '" + GetGithubAuthToken() + @"',
 isMinorRelease: '" + isMinorRelease + @"'
 }
}";
	var appVeyorToken = ReadToken("AppVeyor");
	httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", appVeyorToken);
	httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

	var httpResponseMessage = httpClient.PostAsync("https://ci.appveyor.com/api/builds",
		new System.Net.Http.StringContent(content, System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
	if(httpResponseMessage.IsSuccessStatusCode)
	{
		Information("Release build successfully triggered.");
	}
	else
	{
		Error("Fail to trigger the release build:" + httpResponseMessage.ReasonPhrase);
	}
}

Task("CreateGithubRelease").Description("Create a GitHub release")
	.IsDependentOn("Package")
	.IsDependentOn("GenerateDistributionChannelArtifacts")
	.WithCriteria(!runInDryRun)
	.Does(() =>
{
	var client = GetGithubClient();
	// change timeout to be able to upload the package without getting a timeout
	client.SetRequestTimeout(TimeSpan.FromMinutes(30));

	var releaseNotes = ReadReleaseNotes();

	releaseNotes += Environment.NewLine +  "![Git-Tfs " + _releaseVersion + " download count](https://img.shields.io/github/downloads/" + _forkOwner + "/git-tfs/" + _releaseVersion + "/total.svg)";

	// Check if release already exists (for GitHub Actions re-runs)
	// Use GetAll and filter by TagName for maximum compatibility
	Octokit.Release gitHubRelease = null;
	try
	{
		var allReleases = client.Repository.Release.GetAll(GitHubOwner, GitHubRepository).GetAwaiter().GetResult();
		gitHubRelease = allReleases.FirstOrDefault(r => r.TagName == _releaseVersion);
		
		if(gitHubRelease != null)
		{
			Information($"Github Release '{_releaseVersion}' already exists (Id: {gitHubRelease.Id}). Updating...");
			
			try
			{
				// Update existing release
				var releaseUpdate = gitHubRelease.ToUpdate();
				releaseUpdate.Body = releaseNotes;
				releaseUpdate.Name = _releaseVersion;
				releaseUpdate.TargetCommitish = _sha1;
				
				gitHubRelease = client.Repository.Release.Edit(GitHubOwner, GitHubRepository, gitHubRelease.Id, releaseUpdate).GetAwaiter().GetResult();
			}
			catch (Octokit.ApiException ex)
			{
				throw new Exception($"Failed to update GitHub release '{_releaseVersion}' (Id: {gitHubRelease.Id}): {ex.StatusCode} {ex.Message}", ex);
			}
		}
		else
		{
			// Release doesn't exist, create it
			Information($"Creating new Github Release '{_releaseVersion}'...");
			
			try
			{
				var newRelease = new Octokit.NewRelease(_releaseVersion);
				newRelease.Name = _releaseVersion;
				newRelease.Body = releaseNotes;
				newRelease.Draft = false;
				newRelease.Prerelease = false;
				newRelease.TargetCommitish = _sha1;

				gitHubRelease = client.Repository.Release.Create(GitHubOwner, GitHubRepository, newRelease).GetAwaiter().GetResult();
				Information("Github Release created. Id:" + gitHubRelease.Id);
			}
			catch (Octokit.ApiException ex)
			{
				throw new Exception($"Failed to create GitHub release '{_releaseVersion}': {ex.StatusCode} {ex.Message}", ex);
			}
		}
	}
	catch (System.Exception ex) when (!(ex is Octokit.ApiException))
	{
		throw new Exception("Failed to create or update GitHub release: " + ex.Message, ex);
	}
	
	Information("If needed, delete the Github Release with the command:");
	Information(@".\tools\Cake\Cake.exe build.cake -target=DeleteRelease -idGitHubReleaseToDelete="+ gitHubRelease.Id);
	UploadReleaseAssets(client, gitHubRelease);
});

Task("DeleteRelease").Description("Delete a (broken) GitHub release")
	.WithCriteria(IdGitHubReleaseToDelete != -1)
	.Does(() =>
{
	Information("Deleting release '" + IdGitHubReleaseToDelete +"'...");
	var client = GetGithubClient();
	client.Repository.Release.Delete(GitHubOwner, GitHubRepository, IdGitHubReleaseToDelete).GetAwaiter().GetResult();
});

void UploadReleaseAssets(Octokit.GitHubClient client, Octokit.Release release)
{
	Information("Uploading release assets...");
	
	// Get fresh asset list for idempotent uploads
	var existingAssets = client.Repository.Release.GetAllAssets(GitHubOwner, GitHubRepository, release.Id).GetAwaiter().GetResult();
	
	// Helper function to upload a single asset
	Action<string, string> uploadAsset = (filePath, contentType) => {
		var fileName = System.IO.Path.GetFileName(filePath);
		
		// Check if asset already exists and delete it
		var existingAsset = existingAssets.FirstOrDefault(a => a.Name == fileName);
		if(existingAsset != null)
		{
			Information($"Asset '{fileName}' already exists. Deleting old version...");
			try
			{
				client.Repository.Release.DeleteAsset(GitHubOwner, GitHubRepository, existingAsset.Id).GetAwaiter().GetResult();
			}
			catch (Octokit.ApiException ex)
			{
				Warning($"Failed to delete existing asset '{fileName}': {ex.StatusCode} {ex.Message}");
				// Continue anyway, upload may still succeed
			}
		}
		
		Information($"Uploading asset: {fileName} ({contentType})");
		var fileContents = System.IO.File.OpenRead(filePath);
		
		try
		{
			var assetUpload = new Octokit.ReleaseAssetUpload()
			{
				FileName = fileName,
				ContentType = contentType,
				RawData = fileContents
			};

			client.Repository.Release.UploadAsset(release, assetUpload).GetAwaiter().GetResult();
			Information($"Successfully uploaded: {fileName}");
		}
		catch (Octokit.ApiException ex)
		{
			throw new Exception($"Failed uploading asset '{fileName}' ({contentType}): {ex.StatusCode} {ex.Message}", ex);
		}
		finally
		{
			fileContents.Dispose();
		}
	};
	
	// Upload all assets
	uploadAsset(_zipFilePath, "application/zip");
	uploadAsset(_shaFilePath, "text/plain");
	uploadAsset(_chocolateyManifestZip, "application/zip");
	uploadAsset(_scoopManifestZip, "application/zip");
	uploadAsset(_wingetManifestZip, "application/zip");
	
	Information("All release assets uploaded successfully.");
}

Task("GenerateDistributionChannelArtifacts").Description("Generate packaging artifacts for different distribution channels")
	.IsDependentOn("TagVersion")
	.IsDependentOn("Package")
	.Does(() =>
{
	EnsureDirectoryExists(ChocolateyBuildDir);
	CleanDirectory(ChocolateyBuildDir);

	CopyFiles(@".\build\ChocolateyTemplates\*.*", ChocolateyBuildDir);
	var nuspecPathInBuildDir = System.IO.Path.Combine(ChocolateyBuildDir, "gittfs.nuspec");

	//Template 'chocolateyInstall.ps1'
	var installScriptPathInBuildDir = System.IO.Path.Combine(ChocolateyBuildDir, "chocolateyInstall.ps1");
	string text = TransformTextFile(installScriptPathInBuildDir, "${", "}")
		.WithToken("DownloadUrl", _downloadUrl)
		.WithToken("Checksum", _sha256.ToHex())
		.ToString();
	System.IO.File.WriteAllText(installScriptPathInBuildDir, text);

	var releaseNotes = ReadReleaseNotes();
	if(string.IsNullOrEmpty(releaseNotes))
	{
		releaseNotes = "See " + _downloadUrlBase + "/releases/tag/v" + _semanticVersionShort;
	}
	//http://cakebuild.net/dsl/chocolatey
	Information("Creating Chocolatey package:" + nuspecPathInBuildDir);
	ChocolateyPack(nuspecPathInBuildDir, new ChocolateyPackSettings {
								Version			= _semanticVersionShort,
								ReleaseNotes	= releaseNotes.Split(new string[] { Environment.NewLine }, StringSplitOptions.None),
								OutputDirectory = ChocolateyBuildDir
								});

	var chocolateyPackage = "gittfs." + _semanticVersionShort + ".nupkg";
	var chocolateyPackagePath = System.IO.Path.Combine(ChocolateyBuildDir, chocolateyPackage);

	if(BuildSystem.IsRunningOnAppVeyor)
	{
		Information("Uploading chocolatey package as AppVeyor artifact...");
		BuildSystem.AppVeyor.UploadArtifact(chocolateyPackagePath);
	}
	if(BuildSystem.IsRunningOnAzurePipelinesHosted)
	{
		Information("Uploading chocolatey package as VSTS artifact...");
		BuildSystem.AzurePipelines.Commands.UploadArtifact("install", chocolateyPackagePath, chocolateyPackage);
	}

	var enableChocolateyPush = EnvironmentVariable("ENABLE_CHOCO_PUSH") == "true";

	if(!runInDryRun && enableChocolateyPush)
	{
		ChocolateyPush(chocolateyPackagePath, new ChocolateyPushSettings {
			Source				= "https://chocolatey.org/",
			ApiKey				= GetChocolateyToken(),
			Debug				= false,
			Verbose				= false,
			Force				= false,
			Noop				= false,
			LimitOutput			= false,
			ExecutionTimeout	= 300
			// CacheLocation		= @"C:\temp",
			// AllowUnofficial		= false
		});
	}
	else
	{
		Information($"[DryRun] Would have uploaded chocolatey package '{chocolateyPackagePath}'...");
	}

	// Zip Chocolatey artifacts for release
	_chocolateyManifestZip = System.IO.Path.Combine(buildAssetPath, "manifest.chocolatey.zip");
	Zip(ChocolateyBuildDir, _chocolateyManifestZip);
	Information($"Chocolatey manifest zipped: {_chocolateyManifestZip}");
	
	// Upload Chocolatey manifest zip as artifact
	if(BuildSystem.IsRunningOnAppVeyor)
	{
		BuildSystem.AppVeyor.UploadArtifact(_chocolateyManifestZip);
	}
	if(BuildSystem.IsRunningOnAzurePipelinesHosted)
	{
		BuildSystem.AzurePipelines.Commands.UploadArtifact("install", _chocolateyManifestZip, "manifest.chocolatey.zip");
	}

	// Generate Scoop manifest
	GenerateScoopManifest();

	// Generate WinGet manifest
	GenerateWinGetManifest();
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////
Task("Default").Description("Run the unit tests")
	.IsDependentOn("Run-Unit-Tests");

Task("CIBuild").Description("Do the continuous integration build")
	.IsDependentOn("Run-Unit-Tests")
	//.IsDependentOn("Run-Smoke-Tests") //TFS Projects on CodePlex are no more reachable
	.IsDependentOn("Package");

Task("AppVeyorBuild").Description("Do the continuous integration build with AppVeyor")
	.IsDependentOn("CIBuild")
	.Finally(() =>
	{
		if(BuildSystem.IsRunningOnAppVeyor)
		{
			//Update the AppVeyor build number the latter possible to let the build accessible
			//through the GitHub link until the build end
			UpdateAppVeyorBuildNumber();
		}
	});

Task("AppVeyorRelease").Description("Do the release build with AppVeyor")
	.IsDependentOn("Run-Unit-Tests")
	//.IsDependentOn("Run-Smoke-Tests") //TFS Projects on CodePlex are no more reachable
	.IsDependentOn("Package")
	.IsDependentOn("CreateGithubRelease")
	.Finally(() =>
	{
		if(BuildSystem.IsRunningOnAppVeyor)
		{
			//Update the AppVeyor build number the latter possible to let the build accessible
			//through the GitHub link until the build end
			UpdateAppVeyorBuildNumber();
		}
	});


Task("Release").Description("Build the release and put it on github.com")
	.IsDependentOn("CreateGithubRelease");

Task("DryRunRelease").Description("Do a 'dry-run' release to verify easily most of the release tasks")
	.IsDependentOn("DryRun")
	.IsDependentOn("Release");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////
RunTarget(Target);

//TODO:
// - Improve Release note generation
// - Sonar
// - 'Clean all' Task!
