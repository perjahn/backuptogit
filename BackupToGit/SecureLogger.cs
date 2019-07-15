using System;
using System.Collections.Generic;
using System.IO;

namespace BackupToGit
{
    public class SecureLogger
    {
        public static string Logfile { get; set; } = Path.Combine(Path.GetTempPath(), $"BackupToGit_{DateTime.UtcNow:yyyy-MM-dd}.log");
        public static List<string> SensitiveStrings { get; set; } = new List<string>();

        public static void WriteLine(string message)
        {
            var date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            var clean = message;
            if (SensitiveStrings != null)
            {
                foreach (var replacestring in SensitiveStrings)
                {
                    clean = clean.Replace(replacestring, new string('*', replacestring.Length));
                }
            }

            Console.WriteLine($"{date}: {clean}");
            File.AppendAllText(Logfile, $"{date}: {clean}{Environment.NewLine}");
        }
    }
}
