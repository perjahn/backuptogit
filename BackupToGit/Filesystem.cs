using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BackupToGit
{
    public class Filesystem
    {
        public static void CopyDirectory(string sourceDir, string targetDir)
        {
            var dir = new DirectoryInfo(sourceDir);

            var dirs = dir.GetDirectories();
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var files = dir.GetFiles();
            foreach (var file in files)
            {
                var temppath = Path.Combine(targetDir, file.Name);
                file.CopyTo(temppath, false);
            }

            foreach (var subdir in dirs)
            {
                var temppath = Path.Combine(targetDir, subdir.Name);
                CopyDirectory(subdir.FullName, temppath);
            }
        }

        public static void RobustDelete(string folder)
        {
            if (Directory.Exists(folder))
            {
                var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
                foreach (var filename in files)
                {
                    try
                    {
                        File.SetAttributes(filename, File.GetAttributes(filename) & ~FileAttributes.ReadOnly);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                    {
                        // Will be dealt with when deleting the folder.
                    }
                }

                for (var tries = 1; tries <= 10; tries++)
                {
                    Log($"Deleting folder (try {tries}): '{folder}'");
                    try
                    {
                        Directory.Delete(folder, true);

                        if (!Directory.Exists(folder))
                        {
                            return;
                        }
                        Thread.Sleep(1000);
                        if (!Directory.Exists(folder))
                        {
                            return;
                        }
                    }
                    catch (Exception ex) when (tries < 10 && (ex is UnauthorizedAccessException || ex is IOException))
                    {
                        Thread.Sleep(1000);
                    }
                }
            }
        }

        public static bool CompareFolders(string folder1, string folder2, bool verboseLogging)
        {
            if (!Directory.Exists(folder1) || !Directory.Exists(folder2))
            {
                return false;
            }

            Log($"Retrieving files: '{folder1}'");
            var files1 = Directory.GetFiles(folder1, "*", SearchOption.AllDirectories);
            Log($"Retrieving files: '{folder2}'");
            var files2 = Directory.GetFiles(folder2, "*", SearchOption.AllDirectories);

            if (files1.Length != files2.Length)
            {
                Log($"File count diff: {files1.Length} {files2.Length}");
                return false;
            }

            Array.Sort(files1);
            Array.Sort(files2);

            var diff = false;

            for (var i = 0; i < files1.Length; i++)
            {
                var file1 = files1[i];
                var file2 = files2[i];

                Log($"Comparing: '{file1}' '{file2}'");
                var f1 = file1.Substring(folder1.Length);
                var f2 = file2.Substring(folder2.Length);

                if (f1 != f2)
                {
                    Log($"Filename diff: '{f1}' '{f2}'");
                    diff = true;
                    if (verboseLogging)
                    {
                        continue;
                    }
                    else
                    {
                        return false;
                    }
                }

                var hash1 = GetFileHash(file1);
                var hash2 = GetFileHash(file2);
                if (hash1 != hash2)
                {
                    Log($"Hash diff: '{file1}' '{file2}' {hash1} {hash2}");
                    diff = true;
                    if (verboseLogging)
                    {
                        continue;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return !diff;
        }

        public static string GetFileHash(string filename)
        {
            using var fs = new FileStream(filename, FileMode.Open);
            using var bs = new BufferedStream(fs);
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(bs);
            var formatted = new StringBuilder(2 * hash.Length);
            foreach (var b in hash)
            {
                formatted.AppendFormat("{0:X2}", b);
            }
            return formatted.ToString();
        }

        private static void Log(string message)
        {
            SecureLogger.WriteLine(message);
        }
    }
}
