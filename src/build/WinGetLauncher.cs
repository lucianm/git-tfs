using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

/// <summary>
/// Lightweight launcher for WinGet portable package distribution.
/// Sets working directory to the package location before launching git-tfs.exe,
/// ensuring .NET Framework can resolve DLL dependencies correctly.
/// </summary>
class WinGetLauncher
{
    static int Main(string[] args)
    {
        try
        {
            // Get the current assembly path - when executed via WinGet symlink,
            // this returns the symlink path (size 0), not the physical location
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            
            // Check if we're being executed through a symlink (file size is 0 for symlinks)
            FileInfo fileInfo = new FileInfo(assemblyPath);
            bool isSymlink = (fileInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            bool isZeroSize = fileInfo.Length == 0;
            
            string launcherDir;
            
            if (isZeroSize || isSymlink || Path.GetFileName(assemblyPath).Equals("git-tfs.exe", StringComparison.OrdinalIgnoreCase))
            {
                // We're being executed through a WinGet symlink
                // Search for the real git-tfs-launcher.exe in the Packages folder
                string wingetPackagesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WinGet", "Packages");
                
                string[] foundLaunchers = Directory.GetFiles(wingetPackagesDir, "git-tfs-launcher.exe", SearchOption.AllDirectories);
                
                if (foundLaunchers.Length == 0)
                {
                    Console.Error.WriteLine("Error: Could not find git-tfs-launcher.exe in WinGet Packages folder");
                    return 1;
                }
                
                launcherDir = Path.GetDirectoryName(foundLaunchers[0]);
            }
            else
            {
                // Normal execution, not through symlink
                launcherDir = Path.GetDirectoryName(assemblyPath);
            }
            
            // Path to the real git-tfs.exe in the same directory
            string gitTfsExePath = Path.Combine(launcherDir, "git-tfs.exe");
            
            if (!File.Exists(gitTfsExePath))
            {
                Console.Error.WriteLine("Error: git-tfs.exe not found at: " + gitTfsExePath);
                return 1;
            }
            
            // Prevent recursion: make sure we're not trying to launch ourselves
            // The launcher should be git-tfs-launcher.exe, target should be git-tfs.exe
            string launcherExePath = Path.Combine(launcherDir, "git-tfs-launcher.exe");
            
            if (string.Equals(Path.GetFullPath(gitTfsExePath), Path.GetFullPath(launcherExePath), StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Error: Recursion detected - git-tfs.exe points to launcher itself");
                return 1;
            }
            
            // Configure the process
            var startInfo = new ProcessStartInfo
            {
                FileName = gitTfsExePath,
                UseShellExecute = false,
                CreateNoWindow = false
            };
            
            // Add the launcher directory to PATH so .NET Framework can find DLL dependencies
            // Keep the current working directory intact for git operations
            string currentPath = Environment.GetEnvironmentVariable("PATH");
            startInfo.EnvironmentVariables["PATH"] = launcherDir + Path.PathSeparator + currentPath;
            
            // Pass through all arguments (rebuild command line for .NET Framework 4.0 compatibility)
            if (args.Length > 0)
            {
                string[] quotedArgs = new string[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    // Quote arguments that contain spaces or special chars
                    string arg = args[i];
                    if (arg.Contains(" ") || arg.Contains("\""))
                    {
                        quotedArgs[i] = "\"" + arg.Replace("\"", "\\\"") + "\"";
                    }
                    else
                    {
                        quotedArgs[i] = arg;
                    }
                }
                startInfo.Arguments = string.Join(" ", quotedArgs);
            }
            
            // Launch and wait
            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    Console.Error.WriteLine("Error: Failed to start git-tfs.exe");
                    return 1;
                }
                
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error launching git-tfs: " + ex.Message);
            return 1;
        }
    }
}
