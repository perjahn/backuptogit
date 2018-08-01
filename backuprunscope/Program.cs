using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace backuprunscope
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            BackupToGit.Git git = new BackupToGit.Git();
            BackupToGit.SecureLogger.Logfile = Path.Combine(Directory.GetCurrentDirectory(), "backuprunscope.log");

            if (args.Length != 1)
            {
                Log("Usage: backuprunscope <access_token>");
                return 1;
            }

            string access_token = args[0];
            string folder = "buckets";
            await Runscope.BackupAsync(access_token, folder);

            string gitsourcefolder = folder;
            string gitbinary = Environment.GetEnvironmentVariable("gitbinary");
            string gitserver = Environment.GetEnvironmentVariable("gitserver");
            string gitrepopath = Environment.GetEnvironmentVariable("gitrepopath");
            string gitrepofolder = Environment.GetEnvironmentVariable("gitrepofolder");
            string gitusername = Environment.GetEnvironmentVariable("gitusername");
            string gitpassword = Environment.GetEnvironmentVariable("gitpassword");
            string gitemail = Environment.GetEnvironmentVariable("gitemail");
            bool gitsimulatepush = ParseBooleanEnvironmentVariable("gitsimulatepush", false);

            if (string.IsNullOrEmpty(gitbinary) || string.IsNullOrEmpty(gitserver) || string.IsNullOrEmpty(gitrepopath) || string.IsNullOrEmpty(gitrepofolder) ||
                string.IsNullOrEmpty(gitusername) || string.IsNullOrEmpty(gitpassword) || string.IsNullOrEmpty(gitemail))
            {
                StringBuilder missing = new StringBuilder();
                if (string.IsNullOrEmpty(gitbinary))
                    missing.AppendLine("Missing gitbinary.");
                if (string.IsNullOrEmpty(gitserver))
                    missing.AppendLine("Missing gitserver.");
                if (string.IsNullOrEmpty(gitrepopath))
                    missing.AppendLine("Missing gitrepopath.");
                if (string.IsNullOrEmpty(gitrepofolder))
                    missing.AppendLine("Missing gitrepofolder.");
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
                if (!git.Push(gitbinary, gitsourcefolder, gitserver, gitrepopath, gitrepofolder, gitusername, gitpassword, gitemail, gitsimulatepush))
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
