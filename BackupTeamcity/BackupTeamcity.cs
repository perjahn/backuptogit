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
        var parsedArgs = args.TakeWhile(a => a != "--").ToArray();
        var usage =
@"BackupTeamcity 3.0.0

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

        var noninteractive = parsedArgs.Contains("--noninteractive") ? true : false;
        parsedArgs = parsedArgs.Where(a => a != "--noninteractive").ToArray();

        var verboseLogging = parsedArgs.Contains("--verbose") ? true : false;
        parsedArgs = parsedArgs.Where(a => a != "--verbose").ToArray();

        var result = 0;
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
        var curdir = Environment.CurrentDirectory;

        Log($"Current Directory: '{curdir}'");

        if (!Directory.Exists(sourcefolder))
        {
            var message = $"Couldn't find source folder: '{sourcefolder}'";
            throw new ApplicationException(message);
        }

        if (Directory.Exists(targetfolder))
        {
            Log($"Deleting target folder: '{targetfolder}'");
            BackupToGit.Filesystem.RobustDelete(targetfolder);
        }

        Log($"Creating target folder: '{targetfolder}'");
        Directory.CreateDirectory(targetfolder);


        var shortSourcefolder = sourcefolder;
        if (sourcefolder.StartsWith(curdir + Path.DirectorySeparatorChar))
        {
            shortSourcefolder = sourcefolder.Substring(curdir.Length + 1);
        }


        var includebuildnumberfiles = ParseBooleanEnvironmentVariable("includebuildnumberfiles", false);

        var shortTargetfolder = targetfolder;
        if (targetfolder.StartsWith(curdir + Path.DirectorySeparatorChar))
        {
            shortTargetfolder = targetfolder.Substring(curdir.Length + 1);
        }

        CopyConfigFiles(shortSourcefolder, shortTargetfolder, includebuildnumberfiles);

        var gitbinary = Environment.GetEnvironmentVariable("gitbinary");
        var gitserver = Environment.GetEnvironmentVariable("gitserver");
        var gitrepopath = Environment.GetEnvironmentVariable("gitrepopath");
        var gitreposubpath = Environment.GetEnvironmentVariable("gitreposubpath");
        var gitusername = Environment.GetEnvironmentVariable("gitusername");
        var gitpassword = Environment.GetEnvironmentVariable("gitpassword");
        var gitemail = Environment.GetEnvironmentVariable("gitemail");
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

        var gitsimulatepush = ParseBooleanEnvironmentVariable("gitsimulatepush", false);

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

            Log($"Missing git environment variables, will not push Teamcity config files to Git.{Environment.NewLine}{missing}");
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
                ZipBinary = gitzipbinary ?? string.Empty,
                ZipPassword = gitzippassword ?? string.Empty
            };

            var result = false;
            for (var tries = 0; tries < 5 && !result; tries++)
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

    static void CopyConfigFiles(string sourcefolder, string targetfolder, bool includebuildnumberfiles)
    {
        var files = Directory.GetFiles(sourcefolder, "*", SearchOption.AllDirectories)
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f)
            .ToArray();

        Log($"Found {files.Length} files.");


        var ignorefiles = files
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

        var count = 0;

        foreach (var sourcefile in files)
        {
            var targetfile = Path.Combine(targetfolder, sourcefile.Substring(sourcefolder.Length + 1));

            CopyFile(sourcefile, targetfile);
            count++;
        }

        Log($"Copied {files.Length} files.");
    }

    static void CopyFile(string sourcefile, string targetfile)
    {
        var folder = Path.GetDirectoryName(targetfile);

        if (folder != null && !Directory.Exists(folder))
        {
            Log($"Creating target folder: '{folder}'");
            Directory.CreateDirectory(folder);
        }

        Log($"Copying: '{sourcefile}' -> '{targetfile}'");

        // Also normalize lf to crlf, else later commit/push might fail.
        var rows = File.ReadAllLines(sourcefile);
        File.WriteAllLines(targetfile, rows);
    }

    static void LogColor(string message, ConsoleColor color)
    {
        var oldColor = Console.ForegroundColor;
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
        var hostname = Dns.GetHostName();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        Console.WriteLine($"{hostname}: {now}: {message}");
    }

    static void LogTCSection(string message, IEnumerable<string> collection)
    {
        var hostname = Dns.GetHostName();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var oldColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"##teamcity[blockOpened name='{message}']");
        }
        finally
        {
            Console.ForegroundColor = oldColor;
        }

        Console.WriteLine($"{hostname}: {now}: " + string.Join($"{Environment.NewLine}{hostname}: {now}: ", collection));

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
