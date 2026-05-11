using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using NDesk.Options;
using GitTfs.Core;
using GitTfs.Util;
using StructureMap;

namespace GitTfs.Commands
{
    [Description("init [options] tfs-url-or-instance-name repository-path [git-repository]")]
    public class Init : GitTfsCommand
    {
        private readonly InitOptions _initOptions;
        private readonly RemoteOptions _remoteOptions;
        private readonly Globals _globals;
        private readonly IGitHelpers _gitHelper;
        private readonly AuthorsFile _authorsFileHelper;

        public Init(RemoteOptions remoteOptions, InitOptions initOptions, Globals globals, IGitHelpers gitHelper, AuthorsFile authorsFileHelper)
        {
            _remoteOptions = remoteOptions;
            _gitHelper = gitHelper;
            _authorsFileHelper = authorsFileHelper;
            _globals = globals;
            _initOptions = initOptions;
        }

        public OptionSet OptionSet => _initOptions.OptionSet.Merge(_remoteOptions.OptionSet);

        public bool IsBare => _initOptions.IsBare;

        public IGitHelpers GitHelper => _gitHelper;

        public int Run(string tfsUrl, string tfsRepositoryPath)
        {
            tfsRepositoryPath.AssertValidTfsPathOrRoot();
            DoGitInitDb();
            VerifyGitUserConfig();
            SaveAuthorFileInRepository();
            CommitInitialFiles(_remoteOptions.GitIgnorePath, _remoteOptions.GitAttributesPath);
            UseTheGitIgnoreFile(_remoteOptions.GitIgnorePath);
            UseTheGitAttributesFile(_remoteOptions.GitAttributesPath);
            GitTfsInit(tfsUrl, tfsRepositoryPath);
            return 0;
        }

        private void VerifyGitUserConfig()
        {
            var userName = _globals.Repository.GetConfig<string>("user.name");
            var userEmail = _globals.Repository.GetConfig<string>("user.email");
            if (string.IsNullOrWhiteSpace(userName)
                || string.IsNullOrWhiteSpace(userEmail))
            {
                throw new GitTfsException("Git-tfs requires that the user data in git config should be set. Please configure them before using git-tfs"
                                          + Environment.NewLine + "Actual config: "
                                          + Environment.NewLine + " * user name: " + (string.IsNullOrWhiteSpace(userName) ? "<not set>" : userName)
                                          + Environment.NewLine + " * user email: " + (string.IsNullOrWhiteSpace(userEmail) ? "<not set>" : userEmail)
                                          + Environment.NewLine + "For help on how to set user git config, see https://git-scm.com/book/en/v2/Getting-Started-First-Time-Git-Setup");
            }
        }

        private void SaveAuthorFileInRepository() => _authorsFileHelper.SaveAuthorFileInRepository(_globals.AuthorsFilePath, _globals.GitDir);

        private void CommitInitialFiles(string pathToGitIgnoreFile, string pathToGitAttributesFile)
        {
            if (string.IsNullOrWhiteSpace(pathToGitIgnoreFile) && string.IsNullOrWhiteSpace(pathToGitAttributesFile))
            {
                Trace.WriteLine("No .gitignore or .gitattributes file specified to commit...");
                return;
            }
            _globals.Repository.CommitInitialFiles(pathToGitIgnoreFile, pathToGitAttributesFile);
        }

        private void UseTheGitAttributesFile(string pathToGitAttributesFile)
        {
            if (string.IsNullOrWhiteSpace(pathToGitAttributesFile))
            {
                Trace.WriteLine("No .gitattributes file specified to use...");
                return;
            }
            _globals.Repository.UseGitAttributes(pathToGitAttributesFile);
        }

        private void UseTheGitIgnoreFile(string pathToGitIgnoreFile)
        {
            if (string.IsNullOrWhiteSpace(pathToGitIgnoreFile))
            {
                Trace.WriteLine("No .gitignore file specified to use...");
                return;
            }
            _globals.Repository.UseGitIgnore(pathToGitIgnoreFile);
        }

        public int Run(string tfsUrl, string tfsRepositoryPath, string gitRepositoryPath)
        {
            tfsRepositoryPath.AssertValidTfsPathOrRoot();
            if (!_initOptions.IsBare)
            {
                InitSubdir(gitRepositoryPath);
            }
            else
            {
                Environment.CurrentDirectory = gitRepositoryPath;
                _globals.GitDir = ".";
            }
            var runResult = Run(tfsUrl, tfsRepositoryPath);
            try
            {
                File.WriteAllText(Path.Combine(_globals.GitDir, "description"), tfsRepositoryPath + "\n" + HideUserCredentials(_globals.CommandLineRun));
            }
            catch (Exception)
            {
                Trace.WriteLine("warning: Unable to update the repository description!");
            }
            return runResult;
        }

        public static string HideUserCredentials(string commandLineRun)
        {
            Regex rgx = new Regex("((--|/)username|[/-]u)(=| +)[^ ]+");
            commandLineRun = rgx.Replace(commandLineRun, "--username=xxx");
            rgx = new Regex("((--|/)password|[/-]p)(=| +)[^ ]+");
            return rgx.Replace(commandLineRun, "--password=xxx");
        }

        private void InitSubdir(string repositoryPath)
        {
            if (!Directory.Exists(repositoryPath))
                Directory.CreateDirectory(repositoryPath);
            Environment.CurrentDirectory = repositoryPath;
            _globals.GitDir = ".git";
        }

        private void DoGitInitDb()
        {
            if (!Directory.Exists(_globals.GitDir) || _initOptions.IsBare)
            {
                _gitHelper.CommandNoisy(BuildInitCommand());
            }
            _globals.Repository = _gitHelper.MakeRepository(_globals.GitDir);

            if (!string.IsNullOrWhiteSpace(_initOptions.WorkspacePath))
            {
                Trace.WriteLine("workspace path:" + _initOptions.WorkspacePath);

                try
                {
                    Directory.CreateDirectory(_initOptions.WorkspacePath);
                    _globals.Repository.SetConfig(GitTfsConstants.WorkspaceConfigKey, _initOptions.WorkspacePath);
                }
                catch (Exception)
                {
                    throw new GitTfsException("error: workspace path is invalid!");
                }
            }

            _globals.Repository.SetConfig(GitTfsConstants.IgnoreBranches, false);
            _globals.Repository.SetConfig(GitTfsConstants.IgnoreNotInitBranches, false);
            _globals.Repository.SetConfig("core.autocrlf", _initOptions.GitInitAutoCrlf);

            if (_initOptions.GitInitIgnoreCase != null)
                _globals.Repository.SetConfig("core.ignorecase", _initOptions.GitInitIgnoreCase);
        }

        private string[] BuildInitCommand()
        {
            var initCommand = new List<string> { "init" };
            if (_initOptions.GitInitTemplate != null)
                initCommand.Add("--template=" + _initOptions.GitInitTemplate);
            if (_initOptions.IsBare)
                initCommand.Add("--bare");
            if (_initOptions.GitInitShared is string)
                initCommand.Add("--shared=" + _initOptions.GitInitShared);
            else if (_initOptions.GitInitShared != null)
                initCommand.Add("--shared");
            if (_initOptions.GitInitDefaultBranch != null)
                initCommand.Add("--initial-branch=" + _initOptions.GitInitDefaultBranch);
            return initCommand.ToArray();
        }

        private void GitTfsInit(string tfsUrl, string tfsRepositoryPath) => _globals.Repository.CreateTfsRemote(new RemoteInfo
        {
            Id = _globals.RemoteId,
            Url = tfsUrl,
            Repository = tfsRepositoryPath,
            RemoteOptions = _remoteOptions,
        });
    }
}
