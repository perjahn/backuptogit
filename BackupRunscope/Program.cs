using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupRunscope
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            BackupToGit.Git git = new BackupToGit.Git();
            BackupToGit.SecureLogger.Logfile = Path.Combine(Directory.GetCurrentDirectory(), "BackupRunscope.log");

            string[] parsedArgs = args.TakeWhile(a => a != "--").ToArray();
            string usage = "Usage: BackupRunscope <access_token>";

            if (parsedArgs.Length != 1)
            {
                Log(usage);
                return 1;
            }

            string access_token = parsedArgs[0];
            string folder = "buckets";
            await Runscope.BackupAsync(access_token, folder);

            string gitsourcefolder = folder;
            string gitbinary = Environment.GetEnvironmentVariable("gitbinary");
            string gitserver = Environment.GetEnvironmentVariable("gitserver");
            string gitrepopath = Environment.GetEnvironmentVariable("gitrepopath");
            string gitreposubpath = Environment.GetEnvironmentVariable("gitreposubpath");
            string gitusername = Environment.GetEnvironmentVariable("gitusername");
            string gitpassword = Environment.GetEnvironmentVariable("gitpassword");
            string gitemail = Environment.GetEnvironmentVariable("gitemail");
            bool gitsimulatepush = ParseBooleanEnvironmentVariable("gitsimulatepush", false);

            if (string.IsNullOrEmpty(gitbinary) && File.Exists("/usr/bin/git"))
            {
                gitbinary = "/usr/bin/git";
            }
            if (string.IsNullOrEmpty(gitbinary) && File.Exists(@"C:\Program Files\Git\bin\git.exe"))
            {
                gitbinary = @"C:\Program Files\Git\bin\git.exe";
            }

            if (string.IsNullOrEmpty(gitbinary) || string.IsNullOrEmpty(gitserver) || string.IsNullOrEmpty(gitrepopath) || string.IsNullOrEmpty(gitreposubpath) ||
                string.IsNullOrEmpty(gitusername) || string.IsNullOrEmpty(gitpassword) || string.IsNullOrEmpty(gitemail))
            {
                StringBuilder missing = new StringBuilder();
                if (string.IsNullOrEmpty(gitbinary))
                    missing.AppendLine("Missing gitbinary.");
                if (string.IsNullOrEmpty(gitserver))
                    missing.AppendLine("Missing gitserver.");
                if (string.IsNullOrEmpty(gitrepopath))
                    missing.AppendLine("Missing gitrepopath.");
                if (string.IsNullOrEmpty(gitreposubpath))
                    missing.AppendLine("Missing gitreposubpath.");
                if (string.IsNullOrEmpty(gitusername))
                    missing.AppendLine("Missing gitusername.");
                if (string.IsNullOrEmpty(gitpassword))
                    missing.AppendLine("Missing gitpassword.");
                if (string.IsNullOrEmpty(gitemail))
                    missing.AppendLine("Missing gitemail.");

                Log("Missing git environment variables, will not push Runscope bucket files to Git." + Environment.NewLine + missing.ToString());
            }
            else
            {
                git.GitBinary = gitbinary;
                git.SourceFolder = gitsourcefolder;
                git.Server = gitserver;
                git.RepoPath = gitrepopath;
                git.RepoSubPath = gitreposubpath;
                git.Username = gitusername;
                git.Password = gitpassword;
                git.Email = gitemail;
                git.SimulatePush = gitsimulatepush;

                bool result = false;
                for (int tries = 0; tries < 5 && !result; tries++)
                {
                    result = git.Push();
                }

                if (!result)
                {
                    return 1;
                }
            }

            return 0;
        }

        static bool ParseBooleanEnvironmentVariable(string variableName, bool defaultValue)
        {
            string stringValue = Environment.GetEnvironmentVariable(variableName);
            if (stringValue == null)
            {
                return defaultValue;
            }
            else
            {
                bool boolValue;
                if (!bool.TryParse(stringValue, out boolValue))
                {
                    return defaultValue;
                }
                return boolValue;
            }
        }

        static void Log(string message)
        {
            BackupToGit.SecureLogger.WriteLine(message);
        }
    }
}
