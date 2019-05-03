using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
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


            string url = "https://api.runscope.com/buckets";

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access_token);
                //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string content = await response.Content.ReadAsStringAsync();

                JArray buckets = ((dynamic)JObject.Parse(content)).data;

                Log($"Found {buckets.Count} buckets.");
                int bucketcount = 0;
                int testcount = 0;

                foreach (JToken bucket in buckets.OrderBy(b => (string)((dynamic)b).name.Value, StringComparer.OrdinalIgnoreCase))
                {
                    string bucketname = ((dynamic)bucket).name.Value;
                    string tests_url = ((dynamic)bucket).tests_url.Value;

                    response = await client.GetAsync(tests_url);
                    response.EnsureSuccessStatusCode();
                    content = await response.Content.ReadAsStringAsync();

                    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RunscopeRestDebug")))
                    {
                        File.WriteAllText($"bucketresult_{bucketcount}.json", JToken.Parse(content).ToString());
                    }

                    JArray tests = ((dynamic)JObject.Parse(content)).data;

                    if (tests.Count == 0)
                    {
                        continue;
                    }

                    var savetests = new List<string>();

                    foreach (JToken test in tests.OrderBy(b => (string)((dynamic)b).name.Value, StringComparer.OrdinalIgnoreCase))
                    {
                        string testname = ((dynamic)test).name.Value;
                        string testid = ((dynamic)test).id.Value;

                        string fullname = GetCleanName(bucketname) + "." + GetCleanName(testname);

                        string testurl = $"{tests_url}/{testid}";

                        Log($"Exporting: '{fullname}'");
                        response = await client.GetAsync(testurl);
                        response.EnsureSuccessStatusCode();
                        content = await response.Content.ReadAsStringAsync();

                        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RunscopeRestDebug")))
                        {
                            File.WriteAllText($"testresult_{testcount}.json", JToken.Parse(content).ToString());
                        }

                        dynamic testdetails = ((dynamic)JObject.Parse(content)).data;

                        RemoveElements(testdetails, "exported_at", 7);
                        RemoveElements(testdetails, "id", 7);
                        RemoveElements(testdetails, "last_run", 7);

                        savetests.Add(GetStableSortedJson(testdetails));

                        testcount++;
                    }

                    string filename = Path.Combine(folder, $"{bucketname}.json");

                    string savecontent = "[" + Environment.NewLine +
                        string.Join($",{Environment.NewLine}", savetests.Select(t => "  " + t.Replace(Environment.NewLine, $"{Environment.NewLine}  ")))
                        + Environment.NewLine + "]";

                    Log($"Saving: '{filename}'");
                    File.WriteAllText(filename, savecontent);

                    bucketcount++;
                }

                Log($"Got {testcount} tests.");
            }
        }

        static void RemoveElements(JObject jobject, string name, int depth)
        {
            JToken[] ids = jobject
                .DescendantsAndSelf()
                .Where(o => o.Ancestors().Count() < depth)
                .Select(o => o as JProperty)
                .Where(p => p != null && (p.Name == name))
                .ToArray();

            foreach (var id in ids)
            {
                id.Remove();
            }
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
                var sortedChildren = jtoken.Select(c => GetStableSortedJson(c, level + 1));
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

        static string GetCleanName(string s)
        {
            StringBuilder sb = new StringBuilder();

            foreach (char c in s.ToArray())
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
