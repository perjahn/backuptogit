using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BackupToGit
{
    public class Git
    {
        public bool Push(string gitbinary, string sourcefolder, string server, string repopath, string repofolder, string username, string password, string email, bool gitsimulatepush)
        {
            SecureLogger.SensitiveStrings = new List<string>();
            SecureLogger.SensitiveStrings.AddRange(new[] { username, password });

            if (!File.Exists(gitbinary))
            {
                Log($"Git not found: '{gitbinary}'");
                return false;
            }

            string rootfolder, subfolder;
            int offset = repofolder.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            if (offset >= 0)
            {
                rootfolder = repofolder.Substring(0, offset);
                subfolder = repofolder.Substring(offset + 1);
            }
            else
            {
                rootfolder = repofolder;
                subfolder = ".";
            }

            Filesystem.RobustDelete(rootfolder);


            string url = $"https://{username}:{password}@{server}/{repopath}";

            Log($"Using git url: '{url}'");

            RunCommand(gitbinary, $"--no-pager clone {url}");
            Directory.SetCurrentDirectory(rootfolder);
            Log($"Current directory: '{Directory.GetCurrentDirectory()}'");


            string relativesourcefolder = Path.Combine("..", sourcefolder);
            string targetfolder = subfolder;

            Log("Comparing folders...");
            if (Filesystem.CompareFolders(relativesourcefolder, targetfolder))
            {
                Log($"No changes found: '{relativesourcefolder}' '{targetfolder}'");
                return true;
            }


            if (subfolder != ".")
            {
                Filesystem.RobustDelete(subfolder);
            }

            Log($"Copying files into git folder: '{relativesourcefolder}' -> '{targetfolder}'");
            Filesystem.CopyDirectory(relativesourcefolder, targetfolder);


            Log("Adding/updating/deleting files...");
            RunCommand(gitbinary, "--no-pager add -A");

            Log("Setting config...");
            RunCommand(gitbinary, $"config user.email {email}");
            RunCommand(gitbinary, $"config user.name {username}");

            string date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            string commitmessage = $"Automatic gathering of files: {date}";

            Log("Committing...");
            RunCommand(gitbinary, $"--no-pager commit -m \"{commitmessage}\"");

            Log("Setting config...");
            RunCommand(gitbinary, "config push.default simple");

            Log("Pushing...");
            if (gitsimulatepush)
            {
                Log("...not!");
            }
            else
            {
                RunCommand(gitbinary, "--no-pager push");
            }

            return true;
        }

        private void RunCommand(string binfile, string args)
        {
            Log($"Running: '{binfile}' '{args}'");

            Process process = new Process();
            process.StartInfo = new ProcessStartInfo(binfile, args)
            {
                UseShellExecute = false
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
