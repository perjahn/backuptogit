using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace BackupRunscope
{
    class Runscope
    {
        public static async Task BackupAsync(string access_token, string folder)
        {
            BackupToGit.Filesystem.RobustDelete(folder);
            Directory.CreateDirectory(folder);


            var url = "https://api.runscope.com/buckets";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access_token);

            string bucketsContent;
            using (var response = await client.GetAsync(url))
            {
                response.EnsureSuccessStatusCode();
                bucketsContent = await response.Content.ReadAsStringAsync();
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RunscopeRestDebug")))
            {
                File.WriteAllText($"bucketsresult.json", JToken.Parse(bucketsContent).ToString());
            }

            JArray buckets = ((dynamic)JObject.Parse(bucketsContent)).data;

            Log($"Found {buckets.Count} buckets.");
            var bucketcount = 0;
            var testcount = 0;

            foreach (var bucket in buckets.OrderBy(b => (string)((dynamic)b).name.Value, StringComparer.OrdinalIgnoreCase))
            {
                string bucketname = ((dynamic)bucket).name.Value;
                string tests_url = ((dynamic)bucket).tests_url.Value;

                var baseUri = new UriBuilder(tests_url)
                {
                    Query = "count=10000"
                };

                string bucketContent;

                Log($"Retrieving: '{baseUri.Uri}'");
                using (var response = await client.GetAsync(baseUri.Uri))
                {
                    response.EnsureSuccessStatusCode();
                    bucketContent = await response.Content.ReadAsStringAsync();
                }

                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RunscopeRestDebug")))
                {
                    File.WriteAllText($"bucketresult_{bucketcount}.json", JToken.Parse(bucketContent).ToString());
                }

                JArray tests = ((dynamic)JObject.Parse(bucketContent)).data;

                if (tests.Count == 0)
                {
                    continue;
                }

                var savetests = new List<string>();

                foreach (var test in tests.OrderBy(b => (string)((dynamic)b).name.Value, StringComparer.OrdinalIgnoreCase))
                {
                    var testname = ((dynamic)test).name.Value;
                    var testid = ((dynamic)test).id.Value;

                    var fullname = $"{GetCleanName(bucketname)}.{GetCleanName(testname)}";

                    var testurl = $"{tests_url}/{testid}";

                    string testsContent;

                    Log($"Exporting: '{fullname}'");
                    using var response = await client.GetAsync(testurl);
                    response.EnsureSuccessStatusCode();
                    testsContent = await response.Content.ReadAsStringAsync();

                    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RunscopeRestDebug")))
                    {
                        File.WriteAllText($"testresult_{testcount}.json", JToken.Parse(testsContent).ToString());
                    }

                    dynamic testdetails = ((dynamic)JObject.Parse(testsContent)).data;

                    RemoveElements(testdetails, "exported_at", 7);
                    RemoveElements(testdetails, "id", 7);
                    RemoveElements(testdetails, "last_run", 7);

                    savetests.Add(GetStableSortedJson(testdetails));

                    testcount++;
                }

                var filename = Path.Combine(folder, $"{bucketname}.json");

                var savecontent = "[" + Environment.NewLine +
                    string.Join($",{Environment.NewLine}", savetests.Select(t => "  " + t.Replace(Environment.NewLine, $"{Environment.NewLine}  ")))
                    + Environment.NewLine + "]";

                Log($"Saving: '{filename}'");
                File.WriteAllText(filename, savecontent);

                bucketcount++;
            }

            Log($"Got {testcount} tests.");
        }

        static void RemoveElements(JObject jobject, string name, int depth)
        {
            var ids = jobject
                .DescendantsAndSelf()
                .Where(o => o.Ancestors().Count() < depth)
                .Select(o => o as JProperty)
                .Where(p => p != null && (p.Name == name))
                .ToArray();

            foreach (var id in ids)
            {
                if (id != null)
                {
                    id.Remove();
                }
            }
        }

        static string GetStableSortedJson(JToken jtoken, int level = 0)
        {
            var indent = new string(' ', level * 2);
            var indentChild = new string(' ', (level + 1) * 2);

            if (jtoken.Type == JTokenType.Object)
            {
                var sortedChildren = jtoken.Children().Select(c => GetStableSortedJson(c, level + 1)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase);
                return sortedChildren.Count() == 0 ?
                    "{}" :
                    "{" + $"{Environment.NewLine}{indentChild}" + string.Join($",{Environment.NewLine}{indentChild}", sortedChildren) + $"{Environment.NewLine}{indent}" + "}";
            }
            else if (jtoken.Type == JTokenType.Property)
            {
                var old = (JProperty)jtoken;
                return "\"" + old.Name + "\": " + GetStableSortedJson(old.Value, level);
            }
            else if (jtoken.Type == JTokenType.Array)
            {
                var sortedChildren = jtoken.Select(c => GetStableSortedJson(c, level + 1));
                return sortedChildren.Count() == 0 ?
                    "[]" :
                    $"[{Environment.NewLine}{indentChild}" + string.Join($",{Environment.NewLine}{indentChild}", sortedChildren) + $"{Environment.NewLine}{indent}]";
            }
            else
            {
                // Unknown stuff.
                using var sw = new StringWriter();
                using var jw = new JsonTextWriter(sw);
                jtoken.WriteTo(jw);
                return sw.ToString();
            }
        }

        static string GetCleanName(string s)
        {
            var sb = new StringBuilder();

            foreach (var c in s.ToArray())
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
            BackupToGit.SecureLogger.WriteLine(message);
        }
    }
}
