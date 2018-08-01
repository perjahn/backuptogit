using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace backuprunscope
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length != 1)
            {
                Log("backuprunscope <access_token>");
                return 1;
            }

            string access_token = args[0];

            await BackupAsync(access_token);

            return 0;
        }

        static async Task BackupAsync(string access_token)
        {
            string foldername = "buckets";
            DeleteFolder(foldername);
            Directory.CreateDirectory(foldername);


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
                int testcount = 0;

                foreach (JToken bucket in buckets.OrderBy(b => ((dynamic)b).name.Value))
                {
                    string bucketname = ((dynamic)bucket).name.Value;
                    string tests_url = ((dynamic)bucket).tests_url.Value;

                    response = await client.GetAsync(tests_url);
                    response.EnsureSuccessStatusCode();
                    content = await response.Content.ReadAsStringAsync();

                    JArray tests = ((dynamic)JObject.Parse(content)).data;

                    if (tests.Count == 0)
                    {
                        continue;
                    }

                    testcount += tests.Count;

                    JArray savetests = new JArray();

                    foreach (JToken test in tests.OrderBy(b => ((dynamic)b).name.Value))
                    {
                        string testname = ((dynamic)test).name.Value;
                        string testid = ((dynamic)test).id.Value;

                        string fullname = GetCleanName(bucketname) + "." + GetCleanName(testname);

                        string testurl = $"{tests_url}/{testid}";

                        Log($"Exporting: '{fullname}'");
                        response = await client.GetAsync(testurl);
                        response.EnsureSuccessStatusCode();
                        content = await response.Content.ReadAsStringAsync();

                        dynamic testdetails = ((dynamic)JObject.Parse(content)).data;

                        RemoveElements(testdetails, "exported_at", 7);
                        RemoveElements(testdetails, "id", 7);
                        RemoveElements(testdetails, "last_run", 7);

                        savetests.Add(GetSortedJson(testdetails, 5));
                    }

                    string filename = Path.Combine(foldername, $"{bucketname}.json");
                    Log($"Saving: '{filename}'");
                    File.WriteAllText(filename, savetests.ToString());
                }

                Log($"Got {testcount} tests.");
            }
        }

        static void DeleteFolder(string foldername)
        {
            if (Directory.Exists(foldername))
            {
                for (int tries = 0; tries < 5; tries++)
                {
                    try
                    {
                        Directory.Delete(foldername, true);
                    }
                    catch { }

                    if (!Directory.Exists(foldername))
                    {
                        return;
                    }

                    Thread.Sleep(2000);

                    if (!Directory.Exists(foldername))
                    {
                        return;
                    }
                }
            }

            if (Directory.Exists(foldername))
            {
                Directory.Delete(foldername, true);
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

        static JToken GetSortedJson(JToken jtoken, int depth)
        {
            if (depth > 0 && jtoken.Type == JTokenType.Object)
            {
                JObject old = jtoken as JObject;
                JObject jobject = new JObject();

                foreach (JToken child in old.Children().OrderByDescending(c => c.Path))
                {
                    jobject.AddFirst(GetSortedJson(child, depth - 1));
                }

                return jobject;
            }
            else if (depth > 0 && jtoken.Type == JTokenType.Property)
            {
                JProperty old = jtoken as JProperty;
                JProperty jproperty = new JProperty(old.Name, old.Value);

                foreach (JToken child in old.Children().OrderByDescending(c => c.Path))
                {
                    JToken newchild = GetSortedJson(child, depth - 1);
                    jproperty.Value = newchild;
                }

                return jproperty;
            }
            else if (depth > 0 && jtoken.Type == JTokenType.Array)
            {
                JArray old = jtoken as JArray;
                JArray jarray = new JArray();

                var sortedChildren = old.Select(c => GetSortedJson(c, depth - 1));

                foreach (JToken child in sortedChildren)
                {
                    jarray.Add(child);
                }

                return jarray;
            }
            else
            {
                return jtoken;
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
            Console.WriteLine(message);
        }
    }
}
