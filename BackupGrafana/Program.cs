using System;
using System.IO;
using System.Linq;
using System.Text;

namespace BackupGrafana
{
    class Program
    {
        static int Main(string[] args)
        {
            BackupToGit.Git git = new BackupToGit.Git();
            BackupToGit.SecureLogger.Logfile = Path.Combine(Directory.GetCurrentDirectory(), "BackupGrafana.log");

            if (args.Length != 3)
            {
                Log("Usage: BackupGrafana <serverurl> <username> <password>");
                return 1;
            }

            string url = args[0];
            string username = args[1];
            string password = args[2];
            string folder = "dashboards";

            Grafana grafana = new Grafana();
            if (!grafana.SaveDashboards(url, username, password, folder))
            {
                return 1;
            }

            string gitsourcefolder = folder;
            string gitbinary = Environment.GetEnvironmentVariable("gitbinary");
            string gitserver = Environment.GetEnvironmentVariable("gitserver");
            string gitrepopath = Environment.GetEnvironmentVariable("gitrepopath");
            string gitsubrepopath = Environment.GetEnvironmentVariable("gitsubrepopath");
            string gitusername = Environment.GetEnvironmentVariable("gitusername");
            string gitpassword = Environment.GetEnvironmentVariable("gitpassword");
            string gitemail = Environment.GetEnvironmentVariable("gitemail");
            bool gitsimulatepush = ParseBooleanEnvironmentVariable("gitsimulatepush", false);

            if (string.IsNullOrEmpty(gitbinary) || string.IsNullOrEmpty(gitserver) || string.IsNullOrEmpty(gitrepopath) || string.IsNullOrEmpty(gitsubrepopath) ||
                string.IsNullOrEmpty(gitusername) || string.IsNullOrEmpty(gitpassword) || string.IsNullOrEmpty(gitemail))
            {
                StringBuilder missing = new StringBuilder();
                if (string.IsNullOrEmpty(gitbinary))
                    missing.AppendLine("Missing gitbinary.");
                if (string.IsNullOrEmpty(gitserver))
                    missing.AppendLine("Missing gitserver.");
                if (string.IsNullOrEmpty(gitrepopath))
                    missing.AppendLine("Missing gitrepopath.");
                if (string.IsNullOrEmpty(gitsubrepopath))
                    missing.AppendLine("Missing gitsubrepopath.");
                if (string.IsNullOrEmpty(gitusername))
                    missing.AppendLine("Missing gitusername.");
                if (string.IsNullOrEmpty(gitpassword))
                    missing.AppendLine("Missing gitpassword.");
                if (string.IsNullOrEmpty(gitemail))
                    missing.AppendLine("Missing gitemail.");

                Log("Missing git environment variables, will not push Grafana dashboard files to Git." + Environment.NewLine + missing.ToString());
            }
            else
            {
                git.GitBinary = gitbinary;
                git.SourceFolder = gitsourcefolder;
                git.Server = gitserver;
                git.RepoPath = gitrepopath;
                git.SubRepoPath = gitsubrepopath;
                git.Username = gitusername;
                git.Password = gitpassword;
                git.Email = gitemail;
                git.SimulatePush = gitsimulatepush;

                if (!git.Push())
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
