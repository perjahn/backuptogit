using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupGrafana
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var git = new BackupToGit.Git();
            BackupToGit.SecureLogger.Logfile = Path.Combine(Directory.GetCurrentDirectory(), "BackupGrafana.log");

            var parsedArgs = args.TakeWhile(a => a != "--").ToArray();
            var usage = "Usage: BackupGrafana <serverurl> <username> <password>";

            if (parsedArgs.Length != 3)
            {
                Log(usage);
                return 1;
            }

            var url = parsedArgs[0];
            var username = parsedArgs[1];
            var password = parsedArgs[2];
            var folder = "dashboards";

            var grafana = new Grafana();
            if (!await grafana.SaveDashboards(url, username, password, folder))
            {
                return 1;
            }

            var gitsourcefolder = folder;
            var gitbinary = Environment.GetEnvironmentVariable("gitbinary");
            var gitserver = Environment.GetEnvironmentVariable("gitserver");
            var gitrepopath = Environment.GetEnvironmentVariable("gitrepopath");
            var gitreposubpath = Environment.GetEnvironmentVariable("gitreposubpath");
            var gitusername = Environment.GetEnvironmentVariable("gitusername");
            var gitpassword = Environment.GetEnvironmentVariable("gitpassword");
            var gitemail = Environment.GetEnvironmentVariable("gitemail");
            var gitsimulatepush = ParseBooleanEnvironmentVariable("gitsimulatepush", false);

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
                var missing = new StringBuilder();
                if (string.IsNullOrEmpty(gitbinary))
                {
                    missing.AppendLine("Missing gitbinary.");
                }

                if (string.IsNullOrEmpty(gitserver))
                {
                    missing.AppendLine("Missing gitserver.");
                }

                if (string.IsNullOrEmpty(gitrepopath))
                {
                    missing.AppendLine("Missing gitrepopath.");
                }

                if (string.IsNullOrEmpty(gitreposubpath))
                {
                    missing.AppendLine("Missing gitreposubpath.");
                }

                if (string.IsNullOrEmpty(gitusername))
                {
                    missing.AppendLine("Missing gitusername.");
                }

                if (string.IsNullOrEmpty(gitpassword))
                {
                    missing.AppendLine("Missing gitpassword.");
                }

                if (string.IsNullOrEmpty(gitemail))
                {
                    missing.AppendLine("Missing gitemail.");
                }

                Log("Missing git environment variables, will not push Grafana dashboard files to Git." + Environment.NewLine + missing.ToString());
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

                var result = false;
                for (var tries = 0; tries < 5 && !result; tries++)
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
            var stringValue = Environment.GetEnvironmentVariable(variableName);
            if (stringValue == null)
            {
                return defaultValue;
            }
            else
            {
                if (!bool.TryParse(stringValue, out var boolValue))
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
