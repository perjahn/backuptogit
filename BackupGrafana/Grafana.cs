using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace BackupGrafana
{
    class Grafana
    {
        public async Task<bool> SaveDashboards(string serverurl, string username, string password, string folder)
        {
            BackupToGit.SecureLogger.SensitiveStrings.AddRange(new[] { username, password });

            BackupToGit.Filesystem.RobustDelete(folder);

            Log($"Creating directory: '{folder}'");
            Directory.CreateDirectory(folder);

            using (var client = new HttpClient())
            {
                var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);

                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage result;
                string json;

                Log("Retrieving orgs.");
                string url = $"{serverurl}/api/orgs";
                try
                {
                    result = await client.GetAsync(url);
                    result.EnsureSuccessStatusCode();
                    json = await result.Content.ReadAsStringAsync();
                }
                catch (AggregateException ex)
                {
                    Log($"Couldn't connect to Grafana server: '{url}' '{ex.Message}'");
                    return false;
                }

                var orgs = JArray.Parse(json);
                Log($"Got {orgs.Count} organizations.");

                foreach (dynamic org in orgs)
                {
                    Log($"Switching to org: {org.id} '{org.name}'");
                    url = $"{serverurl}/api/user/using/{org.id}";
                    result = await client.PostAsync(url, null);
                    result.EnsureSuccessStatusCode();
                    json = await result.Content.ReadAsStringAsync();

                    dynamic orgbody = JObject.Parse(json);
                    Log($"Message: {orgbody.message}");

                    await Task.Delay(5000);


                    Log("Searching for dashboards.");
                    url = $"{serverurl}/api/search/";
                    result = await client.GetAsync(url);
                    result.EnsureSuccessStatusCode();
                    json = await result.Content.ReadAsStringAsync();

                    var array = JArray.Parse(json);
                    Log($"Got {array.Count} dashboards.");

                    foreach (dynamic j in array)
                    {
                        Log($"Retrieving dashboard: '{j.uri}'");
                        url = $"{serverurl}/api/dashboards/{j.uri}";
                        result = await client.GetAsync(url);
                        result.EnsureSuccessStatusCode();
                        json = await result.Content.ReadAsStringAsync();

                        dynamic dashboard = JObject.Parse(json);

                        string name = dashboard.meta.slug;

                        string filename = Path.Combine(folder, PrettyName($"{org.name}_{name}") + ".json");

                        string pretty = dashboard.dashboard.ToString(Newtonsoft.Json.Formatting.Indented);

                        Log($"Saving: '{filename}'");
                        File.WriteAllText(filename, pretty);
                    }
                }
            }

            return true;
        }

        string PrettyName(string name)
        {
            var result = new StringBuilder();

            foreach (char c in name.ToCharArray())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }

        static void Log(string message)
        {
            BackupToGit.SecureLogger.WriteLine(message);
        }
    }
}
