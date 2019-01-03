using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupArm
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            BackupToGit.Git git = new BackupToGit.Git();
            BackupToGit.SecureLogger.Logfile = Path.Combine(Directory.GetCurrentDirectory(), "backuparm.log");

            string[] parsedArgs = args.TakeWhile(a => a != "--").ToArray();
            string usage = "Usage: backuparm <folder>";

            if (parsedArgs.Length != 1)
            {
                Log(usage);
                return 1;
            }

            string folder = parsedArgs[0];

            var servicePrincipals = GetServicePrincipals();
            if (servicePrincipals.Length == 0)
            {
                Log("Missing environment variables: AzureTenantId, AzureSubscriptionId, AzureClientId, AzureClientSecret");
                return 1;
            }

            Log($"Got {servicePrincipals.Length} service principals: '{string.Join("', '", servicePrincipals.Select(sp => sp.FriendlyName))}'");

            await Arm.GetAzureAccessTokensAsync(servicePrincipals);

            var accessTokens = servicePrincipals.Where(sp => sp.AccessToken != null).ToArray();
            Log($"Got {accessTokens.Length} access tokens.");
            if (accessTokens.Length == 0)
            {
                return 1;
            }

            var tasks = accessTokens.Select(accessToken => Arm.SaveArmTemplatesAsync(accessToken, folder));
            await Task.WhenAll(tasks);


            string gitsourcefolder = folder;
            string gitbinary = Environment.GetEnvironmentVariable("gitbinary");
            string gitserver = Environment.GetEnvironmentVariable("gitserver");
            string gitrepopath = Environment.GetEnvironmentVariable("gitrepopath");
            string gitsubrepopath = Environment.GetEnvironmentVariable("gitsubrepopath");
            string gitusername = Environment.GetEnvironmentVariable("gitusername");
            string gitpassword = Environment.GetEnvironmentVariable("gitpassword");
            string gitemail = Environment.GetEnvironmentVariable("gitemail");
            bool gitsimulatepush = ParseBooleanEnvironmentVariable("gitsimulatepush", false);
            string gitzipbinary = Environment.GetEnvironmentVariable("gitzipbinary");
            string gitzippassword = Environment.GetEnvironmentVariable("gitzippassword");

            if (string.IsNullOrEmpty(gitbinary) && File.Exists("/usr/bin/git"))
            {
                gitbinary = "/usr/bin/git";
            }
            if (string.IsNullOrEmpty(gitbinary) && File.Exists(@"C:\Program Files\Git\bin\git.exe"))
            {
                gitbinary = @"C:\Program Files\Git\bin\git.exe";
            }

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
                if (string.IsNullOrEmpty(gitzipbinary))
                    missing.AppendLine("Missing gitzipbinary.");
                if (string.IsNullOrEmpty(gitzippassword))
                    missing.AppendLine("Missing gitzippassword.");

                Log("Missing git environment variables, will not push Arm template files to Git." + Environment.NewLine + missing.ToString());
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
                git.ZipBinary = gitzipbinary;
                git.ZipPassword = gitzippassword;

                if (!git.Push())
                {
                    return 1;
                }
            }

            return 0;
        }

        static ServicePrincipal[] GetServicePrincipals()
        {
            string[] validVariables = { "AzureTenantId", "AzureSubscriptionId", "AzureClientId", "AzureClientSecret" };

            var creds =
                Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value)
                .Where(e => validVariables.Any(v => e.Key.Contains(v)))
                .GroupBy(e => new
                {
                    prefix = e.Key.Split(validVariables, StringSplitOptions.None).First(),
                    postfix = e.Key.Split(validVariables, StringSplitOptions.None).Last()
                })
                .OrderBy(c => c.Key.prefix)
                .ThenBy(c => c.Key.postfix);

            var servicePrincipals = new List<ServicePrincipal>();
            foreach (var cred in creds)
            {
                List<string> missingVariables = new List<string>();

                string tenantId = cred.SingleOrDefault(c => c.Key.Contains("AzureTenantId")).Value;
                string subscriptionId = cred.SingleOrDefault(c => c.Key.Contains("AzureSubscriptionId")).Value;
                string clientId = cred.SingleOrDefault(c => c.Key.Contains("AzureClientId")).Value;
                string clientSecret = cred.SingleOrDefault(c => c.Key.Contains("AzureClientSecret")).Value;
                if (tenantId == null)
                {
                    missingVariables.Add("AzureTenantId");
                }
                if (subscriptionId == null)
                {
                    missingVariables.Add("AzureSubscriptionId");
                }
                if (clientId == null)
                {
                    missingVariables.Add("AzureClientId");
                }
                if (clientSecret == null)
                {
                    missingVariables.Add("AzureClientSecret");
                }

                if (missingVariables.Count > 0)
                {
                    Log($"Missing environment variables: Prefix: '{cred.Key.prefix}', Postfix: '{cred.Key.postfix}': '{string.Join("', '", missingVariables)}'");
                }
                else
                {
                    servicePrincipals.Add(new ServicePrincipal
                    {
                        FriendlyName = string.Join('.', (new[] { cred.Key.prefix, cred.Key.postfix }).Where(p => !string.IsNullOrEmpty(p))),
                        TenantId = tenantId,
                        SubscriptionId = subscriptionId,
                        ClientId = clientId,
                        ClientSecret = clientSecret
                    });
                }
            }

            return servicePrincipals.ToArray();
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
                if (!bool.TryParse(stringValue, out bool boolValue))
                {
                    return defaultValue;
                }
                return boolValue;
            }
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}
