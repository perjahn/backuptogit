using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BackupToGit
{
    public class Git
    {
        public string GitBinary { get; set; }
        public string SourceFolder { get; set; }
        public string Server { get; set; }
        public string RepoPath { get; set; }
        public string RepoSubPath { get; set; }
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

            var rootfolder = Path.GetFileNameWithoutExtension(RepoPath);
            Filesystem.RobustDelete(rootfolder);


            var url = $"https://{Username}:{Password}@{Server}/{RepoPath}";

            Log($"Using git url: '{url}'");

            RunCommand(GitBinary, $"--no-pager clone {url}");

            var subrepopath = Path.Combine(rootfolder, RepoSubPath);

            var comparetarget = subrepopath;
            if (!string.IsNullOrEmpty(ZipBinary))
            {
                var decrypt = string.IsNullOrEmpty(ZipPassword) ? string.Empty : $" \"-p{ZipPassword}\"";

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

                var encrypt = string.IsNullOrEmpty(ZipPassword) ? string.Empty : $" -mhe \"-p{ZipPassword}\"";

                Log($"Zipping files into git folder: '{SourceFolder}' -> '{subrepopath}'");
                RunCommand(ZipBinary, $"a -mx9 \"{subrepopath}\" \"{SourceFolder}\"{encrypt}");
            }
            else
            {
                if (RepoSubPath != ".")
                {
                    Filesystem.RobustDelete(subrepopath);
                }

                Log($"Copying files into git subfolder: '{SourceFolder}' -> '{subrepopath}'");
                Filesystem.CopyDirectory(SourceFolder, subrepopath);
            }


            // All git commands will be simpler if executed from repo root.
            var orgdir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(rootfolder);
            Log($"Current directory: '{Directory.GetCurrentDirectory()}'");

            try
            {
                Log("Setting config...");
                RunCommand(GitBinary, $"config user.email {Email}");
                RunCommand(GitBinary, $"config user.name {Username}");
                RunCommand(GitBinary, "config core.ignorecase false");

                Log("Adding/updating/deleting files...");
                RunCommand(GitBinary, "--no-pager add -A");

                var date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                var commitmessage = $"Automatic gathering of files: {date}";

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

            var process = Process.Start(binfile, args);
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
