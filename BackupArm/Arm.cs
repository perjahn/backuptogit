using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BackupArm
{
    class Arm
    {
        static int resultcount = 0;

        public static async Task GetAzureAccessTokensAsync(ServicePrincipal[] servicePrincipals)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                string loginurl = "https://login.microsoftonline.com";
                string managementurlForAuth = "https://management.core.windows.net/";

                foreach (var servicePrincipal in servicePrincipals)
                {
                    string url = $"{loginurl}/{servicePrincipal.TenantId}/oauth2/token?api-version=1.0";
                    string data =
                        $"grant_type=client_credentials&" +
                        $"resource={WebUtility.UrlEncode(managementurlForAuth)}&" +
                        $"client_id={WebUtility.UrlEncode(servicePrincipal.ClientId)}&" +
                        $"client_secret={WebUtility.UrlEncode(servicePrincipal.ClientSecret)}";

                    try
                    {
                        dynamic result = await PostHttpStringAsync(client, url, data, "application/x-www-form-urlencoded");

                        servicePrincipal.AccessToken = result.access_token.Value;
                    }
                    catch (HttpRequestException ex)
                    {
                        Log($"Couldn't get access token for client {servicePrincipal.FriendlyName}: {ex.Message}");
                        servicePrincipal.AccessToken = null;
                    }
                }
            }

            return;
        }

        public static async Task SaveArmTemplatesAsync(ServicePrincipal servicePrincipal, string folder)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", servicePrincipal.AccessToken);
                client.BaseAddress = new Uri("https://management.azure.com");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));


                string url = "/subscriptions?api-version=2016-06-01";

                dynamic result = await GetHttpStringAsync(client, url);
                JArray subscriptions = result.value;

                Log($"{servicePrincipal.FriendlyName}: Found {subscriptions.Count} subscriptions.");

                foreach (dynamic subscription in subscriptions)
                {
                    string subscriptionName = subscription.displayName;
                    subscriptionName = GetCleanName(subscriptionName);

                    url = $"{subscription.id}/resourcegroups?api-version=2018-02-01";

                    result = await GetHttpStringAsync(client, url);
                    JArray resourceGroups = result.value;

                    Log($"{subscriptionName}: Found {resourceGroups.Count} resource groups.");

                    var tasks = resourceGroups.Select(resourceGroup => ExportResourceGroupAsync(client, subscriptionName, (string)resourceGroup["id"], (string)resourceGroup["name"], folder));
                    await Task.WhenAll(tasks);
                }
            }

            return;
        }

        static async Task ExportResourceGroupAsync(HttpClient client, string subscriptionName, string resourceGroupId, string resourceGroupName, string folder)
        {
            // The export api is highly volatile, cannot usually export any resource consistently.
            // Let's try (up to) 12 times, and keep the one with fewest export errors.

            string url = $"{resourceGroupId}/exportTemplate?api-version=2015-01-01";
            JObject jobject = JObject.Parse("{\"options\": \"IncludeParameterDefaultValue\", \"resources\": [\"*\"]}");

            var results = new List<JObject>();

            for (int tries = 0; tries < 12; tries++)
            {
                try
                {
                    results.Add(await PostHttpStringAsync(client, url, jobject));
                }
                catch (IOException ex)
                {
                    Log($"Couldn't post to (try {tries}): {Environment.NewLine}'{url}'{Environment.NewLine}'{jobject.ToString()}'{Environment.NewLine}{ex.ToString()}");
                }

                await Task.Delay(5000);
            }

            int minerrors = results.Min(o => GetErrorCount(o));
            int maxerrors = results.Max(o => GetErrorCount(o));
            JObject result = results.First(o => GetErrorCount(o) == minerrors);

            string filename = Path.Combine(folder, subscriptionName, $"{resourceGroupName}.json");

            jobject = ScrubSecrets(result, filename);
            jobject = DeleteSpuriousErrors(jobject,
                new[] { "Microsoft.Network/networkSecurityGroups",
                        "Microsoft.Sql/servers/databases/syncGroups",
                        "Microsoft.Sql/servers/databases/vulnerabilityAssessments",
                        "Microsoft.Sql/servers/firewallRules",
                        "Microsoft.Storage/storageAccounts/blobServices",
                        "Microsoft.Web/sites/deployments",
                        "Microsoft.Web/sites/slots/deployments" },
                filename);
            string json = GetStableSortedJson(jobject);

            string folderPath = Path.GetDirectoryName(filename);
            if (!Directory.Exists(folderPath))
            {
                Log($"Creating folder: '{folderPath}'");
                Directory.CreateDirectory(folderPath);
            }

            Log($"Saving: '{filename}'" + (minerrors != maxerrors ? $" (Got {minerrors}-{maxerrors} errors)" : string.Empty));
            File.WriteAllText(filename, json);
        }

        static int GetErrorCount(JObject jobject)
        {
            if (jobject["error"] == null || jobject["error"]["details"] == null)
            {
                return 0;
            }

            JArray details = (JArray)jobject["error"]["details"];

            return details.Count;
        }

        static JObject ScrubSecrets(JObject jobject, string name)
        {
            string[] propertyNames = { "keyData", "xmlCfg", "commandToExecute" };

            var properties = jobject
                .DescendantsAndSelf()
                .Where(d => d is JProperty && propertyNames.Contains(((JProperty)d).Name))
                .Select(d => (JProperty)d);

            foreach (var property in properties)
            {
                Log($"Removing {property.Name} value from: '{name}'");
                property.Value = string.Empty;
            }

            return jobject;
        }

        static JObject DeleteSpuriousErrors(JObject jobject, string[] errorTypes, string name)
        {
            if (jobject["error"] == null || jobject["error"]["details"] == null)
            {
                return jobject;
            }

            JArray details = (JArray)jobject["error"]["details"];

            for (int i = 0; i < details.Count();)
            {
                string target = (string)details[i]["target"];

                if (errorTypes.Contains(target))
                {
                    Log($"Removing {target} value from: '{name}'");
                    details.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }

            return jobject;
        }

        static string GetStableSortedJson(JToken jtoken, int level = 0)
        {
            string indent = new string(' ', level * 2);
            string indentChild = new string(' ', (level + 1) * 2);

            if (jtoken.Type == JTokenType.Object)
            {
                var sortedChildren = jtoken.Children().Select(c => GetStableSortedJson(c, level + 1)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase);
                return sortedChildren.Count() == 0 ?
                    "{}" :
                    "{" + Environment.NewLine + indentChild + string.Join($",{Environment.NewLine}{indentChild}", sortedChildren) + Environment.NewLine + indent + "}";
            }
            else if (jtoken.Type == JTokenType.Property)
            {
                JProperty old = (JProperty)jtoken;
                return "\"" + old.Name + "\": " + GetStableSortedJson(old.Value, level);
            }
            else if (jtoken.Type == JTokenType.Array)
            {
                var sortedChildren = jtoken.Select(c => GetStableSortedJson(c, level + 1)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase);
                return sortedChildren.Count() == 0 ?
                    "[]" :
                    "[" + Environment.NewLine + indentChild + string.Join($",{Environment.NewLine}{indentChild}", sortedChildren) + Environment.NewLine + indent + "]";
            }
            else
            {
                // Unknown stuff.
                using (var sw = new StringWriter())
                {
                    using (var jw = new JsonTextWriter(sw))
                    {
                        jtoken.WriteTo(jw);
                        return sw.ToString();
                    }
                }
            }
        }

        static async Task<JObject> GetHttpStringAsync(HttpClient client, string url)
        {
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string result = await response.Content.ReadAsStringAsync();

            if (result.Length > 0)
            {
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ArmRestDebug")))
                {
                    File.WriteAllText($"result_{resultcount++}.json", JToken.Parse(result).ToString());
                }
                return JObject.Parse(result);
            }

            return null;
        }

        static async Task<JObject> PostHttpStringAsync(HttpClient client, string url, JToken jsoncontent)
        {
            var response = await client.PostAsync(url, new StringContent(jsoncontent.ToString(), Encoding.UTF8, "application/json"));
            response.EnsureSuccessStatusCode();
            string result = await response.Content.ReadAsStringAsync();

            if (result.Length > 0)
            {
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ArmRestDebug")))
                {
                    File.WriteAllText($"result_{resultcount++}.json", JToken.Parse(result).ToString());
                }
                return JObject.Parse(result);
            }

            return null;
        }

        static async Task<JObject> PostHttpStringAsync(HttpClient client, string url, string content, string contenttype)
        {
            var response = await client.PostAsync(url, new StringContent(content, Encoding.UTF8, contenttype));
            response.EnsureSuccessStatusCode();
            string result = await response.Content.ReadAsStringAsync();

            if (result.Length > 0)
            {
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ArmRestDebug")))
                {
                    File.WriteAllText($"result_{resultcount++}.json", JToken.Parse(result).ToString());
                }
                return JObject.Parse(result);
            }

            return null;
        }

        static string GetCleanName(string s)
        {
            StringBuilder sb = new StringBuilder();

            foreach (char c in s.ToCharArray())
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-')
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}
