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
            var git = new BackupToGit.Git();
            BackupToGit.SecureLogger.Logfile = Path.Combine(Directory.GetCurrentDirectory(), "backuparm.log");

            var parsedArgs = args.TakeWhile(a => a != "--").ToArray();
            var usage = "Usage: backuparm <folder>";

            if (parsedArgs.Length != 1)
            {
                Log(usage);
                return 1;
            }

            var folder = parsedArgs[0];

            ServicePrincipal[] servicePrincipals;

            string[] files = Directory.GetFiles(".", "*BearerToken*.txt")
                .Select(f => f.StartsWith($".{Path.DirectorySeparatorChar}") || f.StartsWith($".{Path.AltDirectorySeparatorChar}") ? f.Substring(2) : f)
                .ToArray();
            if (files.Length >= 1)
            {
                servicePrincipals = new ServicePrincipal[files.Length];
                Log($"Found {files.Length} token files.");
                for (int i = 0; i < files.Length; i++)
                {
                    var filename = files[i];
                    string content = File.ReadAllText(filename);
                    servicePrincipals[i] = new ServicePrincipal
                    {
                        AccessToken = content,
                        FriendlyName = filename
                    };
                }
            }
            else
            {
                servicePrincipals = GetServicePrincipals();
                if (servicePrincipals.Length == 0)
                {
                    Log("Missing environment variables: AzureTenantId, AzureSubscriptionId, AzureClientId, AzureClientSecret");
                    return 1;
                }

                Log($"Got {servicePrincipals.Length} service principals: '{string.Join("', '", servicePrincipals.Select(sp => sp.FriendlyName))}'");

                await Arm.GetAzureAccessTokensAsync(servicePrincipals);

                servicePrincipals = servicePrincipals.Where(sp => string.IsNullOrEmpty(sp.AccessToken)).ToArray();
                Log($"Got {servicePrincipals.Length} access tokens.");
                if (servicePrincipals.Length == 0)
                {
                    return 1;
                }
            }


            var tasks = servicePrincipals.Select(servicePrincipal => Arm.SaveArmTemplatesAsync(servicePrincipal, folder));
            await Task.WhenAll(tasks);


            var gitsourcefolder = folder;
            var gitbinary = Environment.GetEnvironmentVariable("gitbinary");
            var gitserver = Environment.GetEnvironmentVariable("gitserver");
            var gitrepopath = Environment.GetEnvironmentVariable("gitrepopath");
            var gitreposubpath = Environment.GetEnvironmentVariable("gitreposubpath");
            var gitusername = Environment.GetEnvironmentVariable("gitusername");
            var gitpassword = Environment.GetEnvironmentVariable("gitpassword");
            var gitemail = Environment.GetEnvironmentVariable("gitemail");
            var gitsimulatepush = ParseBooleanEnvironmentVariable("gitsimulatepush", false);
            var gitzipbinary = Environment.GetEnvironmentVariable("gitzipbinary");
            var gitzippassword = Environment.GetEnvironmentVariable("gitzippassword");

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

                if (string.IsNullOrEmpty(gitzipbinary))
                {
                    missing.AppendLine("Missing gitzipbinary.");
                }

                if (string.IsNullOrEmpty(gitzippassword))
                {
                    missing.AppendLine("Missing gitzippassword.");
                }

                Log("Missing git environment variables, will not push Arm template files to Git." + Environment.NewLine + missing.ToString());
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
                git.ZipBinary = gitzipbinary ?? string.Empty;
                git.ZipPassword = gitzippassword ?? string.Empty;

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

        static ServicePrincipal[] GetServicePrincipals()
        {
            string[] validVariables = { "AzureTenantId", "AzureClientId", "AzureClientSecret" };

            var creds =
                Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)(e.Value ?? string.Empty))
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
                var missingVariables = new List<string>();

                var tenantId = cred.SingleOrDefault(c => c.Key.Contains("AzureTenantId")).Value;
                var clientId = cred.SingleOrDefault(c => c.Key.Contains("AzureClientId")).Value;
                var clientSecret = cred.SingleOrDefault(c => c.Key.Contains("AzureClientSecret")).Value;
                if (tenantId == null)
                {
                    missingVariables.Add("AzureTenantId");
                }
                if (clientId == null)
                {
                    missingVariables.Add("AzureClientId");
                }
                if (clientSecret == null)
                {
                    missingVariables.Add("AzureClientSecret");
                }

                if (tenantId == null || clientId == null || clientSecret == null || missingVariables.Count > 0)
                {
                    Log($"Missing environment variables: Prefix: '{cred.Key.prefix}', Postfix: '{cred.Key.postfix}': '{string.Join("', '", missingVariables)}'");
                }
                else
                {
                    servicePrincipals.Add(new ServicePrincipal
                    {
                        FriendlyName = string.Join('.', (new[] { cred.Key.prefix, cred.Key.postfix }).Where(p => !string.IsNullOrEmpty(p))),
                        TenantId = tenantId,
                        ClientId = clientId,
                        ClientSecret = clientSecret
                    });
                }
            }

            return servicePrincipals.ToArray();
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
            Console.WriteLine(message);
        }
    }
}
