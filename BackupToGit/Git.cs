using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BackupToGit
{
    public class Git
    {
        public string GitBinary { get; set; }
        public string SourceFolder { get; set; }
        public string Server { get; set; }
        public string RepoPath { get; set; }
        public string SubRepoPath { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public bool SimulatePush { get; set; }

        public string ZipBinary { get; set; }
        public string ZipPassword { get; set; }

        public bool VerboseLogging { get; set; } = false;

        public bool Push()
        {
            SecureLogger.SensitiveStrings = new List<string>();
            SecureLogger.SensitiveStrings.AddRange(new[] { Username, Password });
            if (!string.IsNullOrEmpty(ZipPassword))
            {
                SecureLogger.SensitiveStrings.Add(ZipPassword);
            }

            if (!Directory.Exists(SourceFolder))
            {
                Log($"Source folder not found: '{SourceFolder}'");
                return false;
            }
            if (!File.Exists(GitBinary))
            {
                Log($"Git binary not found: '{GitBinary}'");
                return false;
            }

            string rootfolder = Path.GetFileNameWithoutExtension(RepoPath);
            Filesystem.RobustDelete(rootfolder);


            string url = $"https://{Username}:{Password}@{Server}/{RepoPath}";

            Log($"Using git url: '{url}'");

            RunCommand(GitBinary, $"--no-pager clone {url}");

            string subrepopath = Path.Combine(rootfolder, SubRepoPath);

            string comparetarget = subrepopath;
            if (!string.IsNullOrEmpty(ZipBinary))
            {
                string decrypt = string.IsNullOrEmpty(ZipPassword) ? string.Empty : $" \"-p{ZipPassword}\"";

                comparetarget = Path.Combine("tmp", Path.GetFileName(SourceFolder));

                Filesystem.RobustDelete("tmp");
                if (File.Exists(subrepopath))
                {
                    RunCommand(ZipBinary, $"x \"{subrepopath}\" -otmp{decrypt}");
                }
            }


            Log("Comparing folders...");
            if (Filesystem.CompareFolders(SourceFolder, comparetarget, VerboseLogging))
            {
                Log($"No changes found: '{SourceFolder}' '{comparetarget}'");
                return true;
            }


            if (!string.IsNullOrEmpty(ZipBinary))
            {
                File.Delete(subrepopath);

                string encrypt = string.IsNullOrEmpty(ZipPassword) ? string.Empty : $" -mhe \"-p{ZipPassword}\"";

                Log($"Zipping files into git folder: '{SourceFolder}' -> '{subrepopath}'");
                RunCommand(ZipBinary, $"a -mx9 \"{subrepopath}\" \"{SourceFolder}\"{encrypt}");
            }
            else
            {
                if (SubRepoPath != ".")
                {
                    Filesystem.RobustDelete(subrepopath);
                }

                Log($"Copying files into git subfolder: '{SourceFolder}' -> '{subrepopath}'");
                Filesystem.CopyDirectory(SourceFolder, subrepopath);
            }


            // All git commands will be simpler if executed from repo root.
            string orgdir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(rootfolder);
            Log($"Current directory: '{Directory.GetCurrentDirectory()}'");

            try
            {
                Log("Adding/updating/deleting files...");
                RunCommand(GitBinary, "--no-pager add -A");

                Log("Setting config...");
                RunCommand(GitBinary, $"config user.email {Email}");
                RunCommand(GitBinary, $"config user.name {Username}");
                RunCommand(GitBinary, "config core.ignorecase false");

                string date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                string commitmessage = $"Automatic gathering of files: {date}";

                Log("Committing...");
                RunCommand(GitBinary, $"--no-pager commit -m \"{commitmessage}\"");

                Log("Setting config...");
                RunCommand(GitBinary, "config push.default simple");

                Log("Pushing...");
                if (SimulatePush)
                {
                    Log("...not!");
                }
                else
                {
                    RunCommand(GitBinary, "--no-pager push");
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(orgdir);
                Log($"Current directory: '{Directory.GetCurrentDirectory()}'");
            }

            return true;
        }

        private void RunCommand(string binfile, string args)
        {
            Log($"Running: '{binfile}' '{args}'");

            Process process = new Process
            {
                StartInfo = new ProcessStartInfo(binfile, args)
                {
                    UseShellExecute = false
                }
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Failed to execute: '{binfile}', args: '{args}'");
            }

            return;
        }

        private void Log(string message)
        {
            SecureLogger.WriteLine(message);
        }
    }
}
