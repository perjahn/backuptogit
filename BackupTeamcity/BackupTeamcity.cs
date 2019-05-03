using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

public class Program
{
    public static int Main(string[] args)
    {
        string[] parsedArgs = args.TakeWhile(a => a != "--").ToArray();
        string usage =
@"BackupTeamcity 2.0.1

This is a backup program that retrieves all important configuration files on
a Teamcity build server. These files can be backuped and later imported on
any other build server. Junk files are excluded.

Reason for not using Teamcity's own backup feature is that it will make too
many commits, one for each change. This tool can instead be scheduled once
a day.

Usage: BackupTeamcity [--noninteractive] [--verbose] <source> <target>

Example: BackupTeamcity D:\TeamCity\.BuildServer\config\projects _Artifacts\projects

Optional environment variables (default):
includebuildnumberfiles  - true/(false)

Optional environment variables, used for pushing to git (with examples):
gitbinary                - C:\Program Files\Git\git.exe
gitserver                - gitserver.organization.com
gitrepopath              - organization/tcconfig.git
gitreposubpath           - projects, subfolder within the repo.
gitusername              - luser
gitpassword              - abc123
gitemail                 - noreply@example.com
gitsimulatepush          - true/(false)

Repo will be cloned from url: https://gitusername:gitpassword@gitserver/gitrepopath";

        bool noninteractive = parsedArgs.Contains("--noninteractive") ? true : false;
        parsedArgs = parsedArgs.Where(a => a != "--noninteractive").ToArray();

        bool verboseLogging = parsedArgs.Contains("--verbose") ? true : false;
        parsedArgs = parsedArgs.Where(a => a != "--verbose").ToArray();

        int result = 0;
        try
        {
            if (parsedArgs.Length != 2)
            {
                Console.WriteLine(usage);
                return 1;
            }

            BackupTeamcity(parsedArgs[0], parsedArgs[1], verboseLogging);
        }
        catch (ApplicationException ex)
        {
            LogColor(ex.Message, ConsoleColor.Red);
            result = 1;
        }
        catch (Exception ex)
        {
            LogColor(ex.ToString(), ConsoleColor.Red);
            result = 1;
        }

        if (!noninteractive)
        {
            Log("Press any key to continue...");
            Console.ReadKey();
        }

        return result;
    }

    static void BackupTeamcity(string sourcefolder, string targetfolder, bool verboseLogging)
    {
        string curdir = Environment.CurrentDirectory;

        Log($"Current Directory: '{curdir}'");

        if (!Directory.Exists(sourcefolder))
        {
            string message = $"Couldn't find source folder: '{sourcefolder}'";
            throw new ApplicationException(message);
        }

        if (Directory.Exists(targetfolder))
        {
            Log($"Deleting target folder: '{targetfolder}'");
            BackupToGit.Filesystem.RobustDelete(targetfolder);
        }

        Log($"Creating target folder: '{targetfolder}'");
        Directory.CreateDirectory(targetfolder);


        string shortSourcefolder = sourcefolder;
        if (sourcefolder.StartsWith(curdir + Path.DirectorySeparatorChar))
        {
            shortSourcefolder = sourcefolder.Substring(curdir.Length + 1);
        }


        bool includebuildnumberfiles = ParseBooleanEnvironmentVariable("includebuildnumberfiles", false);

        string shortTargetfolder = targetfolder;
        if (targetfolder.StartsWith(curdir + Path.DirectorySeparatorChar))
        {
            shortTargetfolder = targetfolder.Substring(curdir.Length + 1);
        }

        CopyConfigFiles(shortSourcefolder, shortTargetfolder, includebuildnumberfiles);

        string gitbinary = Environment.GetEnvironmentVariable("gitbinary");
        string gitserver = Environment.GetEnvironmentVariable("gitserver");
        string gitrepopath = Environment.GetEnvironmentVariable("gitrepopath");
        string gitreposubpath = Environment.GetEnvironmentVariable("gitreposubpath");
        string gitusername = Environment.GetEnvironmentVariable("gitusername");
        string gitpassword = Environment.GetEnvironmentVariable("gitpassword");
        string gitemail = Environment.GetEnvironmentVariable("gitemail");
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

        bool gitsimulatepush = ParseBooleanEnvironmentVariable("gitsimulatepush", false);

        if (string.IsNullOrEmpty(gitbinary) || string.IsNullOrEmpty(gitserver) || string.IsNullOrEmpty(gitrepopath) || string.IsNullOrEmpty(gitreposubpath) ||
            string.IsNullOrEmpty(gitusername) || string.IsNullOrEmpty(gitpassword) || string.IsNullOrEmpty(gitemail))
        {
            StringBuilder missing = new StringBuilder();
            if (string.IsNullOrEmpty(gitbinary))
                missing.AppendLine("Missing gitbinary.");
            if (string.IsNullOrEmpty(gitserver))
                missing.AppendLine("Missing gitserver.");
            if (string.IsNullOrEmpty(gitrepopath))
                missing.AppendLine("Missing gitrepopath.");
            if (string.IsNullOrEmpty(gitreposubpath))
                missing.AppendLine("Missing gitreposubpath.");
            if (string.IsNullOrEmpty(gitusername))
                missing.AppendLine("Missing gitusername.");
            if (string.IsNullOrEmpty(gitpassword))
                missing.AppendLine("Missing gitpassword.");
            if (string.IsNullOrEmpty(gitemail))
                missing.AppendLine("Missing gitemail.");

            Log("Missing git environment variables, will not push Teamcity config files to Git." + Environment.NewLine + missing.ToString());
        }
        else
        {
            var git = new BackupToGit.Git()
            {
                GitBinary = gitbinary,
                SourceFolder = shortTargetfolder,
                Server = gitserver,
                RepoPath = gitrepopath,
                RepoSubPath = gitreposubpath,
                Username = gitusername,
                Password = gitpassword,
                Email = gitemail,
                SimulatePush = gitsimulatepush,
                VerboseLogging = verboseLogging,
                ZipBinary = gitzipbinary,
                ZipPassword = gitzippassword
            };

            bool result = false;
            for (int tries = 0; tries < 5 && !result; tries++)
            {
                result = git.Push();
            }

            if (!result)
            {
                return;
            }
        }
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

    static void CopyFolder(string sourcefolder, string targetfolder)
    {
        string[] files = Directory.GetFiles(sourcefolder, "*", SearchOption.AllDirectories)
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f)
            .ToArray();

        Log($"Copying {files.Length} files to: '{targetfolder}'");

        int count = 0;

        foreach (string sourcefile in files)
        {
            string targetfile = Path.Combine(targetfolder, sourcefile.Substring(sourcefolder.Length + 1));

            string folder = Path.GetDirectoryName(targetfile);

            if (!Directory.Exists(folder))
            {
                Log($"Creating target folder: '{folder}'");
                Directory.CreateDirectory(folder);
            }

            Log($"Copying: '{sourcefile}' -> '{targetfile}'");

            File.Copy(sourcefile, targetfile, true);
            count++;
        }

        Log($"Copied {files.Length} files.");
    }

    static void CopyConfigFiles(string sourcefolder, string targetfolder, bool includebuildnumberfiles)
    {
        string[] files = Directory.GetFiles(sourcefolder, "*", SearchOption.AllDirectories)
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f)
            .ToArray();

        Log($"Found {files.Length} files.");


        string[] ignorefiles = files
            .Where(f => Path.GetExtension(f).Length == 2 && char.IsDigit(Path.GetExtension(f)[1]))
            .ToArray();
        files = files
            .Where(f => !ignorefiles.Contains(f))
            .ToArray();
        LogTCSection($"Ignoring {ignorefiles.Length} backup files.", ignorefiles);


        ignorefiles = files
            .Where(f => f.EndsWith(".buildNumbers.properties"))
            .ToArray();
        if (includebuildnumberfiles)
        {
            Log($"Including {ignorefiles.Length} build number files.");
        }
        else
        {
            files = files
                .Where(f => !ignorefiles.Contains(f))
                .ToArray();
            LogTCSection($"Ignoring {ignorefiles.Length} build number files.", ignorefiles);
        }

        ignorefiles = files
            .Where(f => Path.GetFileName(f) == "plugin-settings.xml" && new FileInfo(f).Length == 56)
            .ToArray();
        files = files
            .Where(f => !ignorefiles.Contains(f))
            .ToArray();
        LogTCSection($"Ignoring {ignorefiles.Length} default plugin settings files.", ignorefiles);


        Log($"Backuping {files.Length} files to: '{targetfolder}'");

        int count = 0;

        foreach (string sourcefile in files)
        {
            string targetfile = Path.Combine(targetfolder, sourcefile.Substring(sourcefolder.Length + 1));

            CopyFile(sourcefile, targetfile);
            count++;
        }

        Log($"Copied {files.Length} files.");
    }

    static void CopyFile(string sourcefile, string targetfile)
    {
        string folder = Path.GetDirectoryName(targetfile);

        if (!Directory.Exists(folder))
        {
            Log($"Creating target folder: '{folder}'");
            Directory.CreateDirectory(folder);
        }

        Log($"Copying: '{sourcefile}' -> '{targetfile}'");

        // Also normalize lf to crlf, else later commit/push might fail.
        string[] rows = File.ReadAllLines(sourcefile);
        File.WriteAllLines(targetfile, rows);
    }

    static void LogColor(string message, ConsoleColor color)
    {
        ConsoleColor oldColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Log(message);
        }
        finally
        {
            Console.ForegroundColor = oldColor;
        }
    }

    static void Log(string message)
    {
        string hostname = Dns.GetHostName();
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        Console.WriteLine($"{hostname}: {now}: {message}");
    }

    static T LogTCSection<T>(string message, Func<T> func)
    {
        ConsoleColor oldColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"##teamcity[blockOpened name='{message}']");
        }
        finally
        {
            Console.ForegroundColor = oldColor;
        }

        T result = func.Invoke();

        oldColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"##teamcity[blockClosed name='{message}']");
        }
        finally
        {
            Console.ForegroundColor = oldColor;
        }

        return result;
    }

    static void LogTCSection(string message, IEnumerable<string> collection)
    {
        string hostname = Dns.GetHostName();
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        ConsoleColor oldColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"##teamcity[blockOpened name='{message}']");
        }
        finally
        {
            Console.ForegroundColor = oldColor;
        }

        Console.WriteLine($"{hostname}: {now}: " + string.Join(Environment.NewLine + $"{hostname}: {now}: ", collection));

        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"##teamcity[blockClosed name='{message}']");
        }
        finally
        {
            Console.ForegroundColor = oldColor;
        }
    }
}
