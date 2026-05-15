using BaseLibrary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace AutoUpdaterModel;

public static class Services
{
    public const int ExpectedArgCount = 7;
    private const string BackupSuffix = ".update.bak";
    private static readonly string[] ReservedTopLevelNames = { "AutoUpdater", "Repository" };

    public static string? ProcessArg(
        string[] args,
        out Version? versionOld,
        out Version? versionNew,
        out string? manifestUrl,
        out string? folderToInstall,
        out string? emailToReportIssue,
        out string? nameProgram,
        out int callerPid)
    {
        versionOld = versionNew = null;
        manifestUrl = folderToInstall = emailToReportIssue = nameProgram = null;
        callerPid = 0;

        if (args.Length == 0)
            return "no argument";

        if (args.Length != ExpectedArgCount)
        {
            return $"It is necessary {ExpectedArgCount} arguments: " +
                   "old version; new version; manifest URL; folder to install; " +
                   "email to report issue; program name; caller process id";
        }

        if (!Version.TryParse(args[0], out versionOld))
            return $"Old version is not in a valid format: '{args[0]}'";

        if (!Version.TryParse(args[1], out versionNew))
            return $"New version is not in a valid format: '{args[1]}'";

        manifestUrl = args[2];
        folderToInstall = args[3];
        emailToReportIssue = args[4];
        nameProgram = args[5];

        if (!int.TryParse(args[6], out callerPid) || callerPid < 0)
            return $"Caller process id is not a valid integer: '{args[6]}'";

        return null;
    }

    /// <summary>
    /// Replaces the contents of <paramref name="folderToInstall"/> with the contents of
    /// each zip in <paramref name="folderRepository"/>. The replacement is atomic with
    /// rollback on failure. The folder identified by <paramref name="folderToPreserve"/>
    /// (typically the running updater's own folder) is left untouched.
    /// </summary>
    public static string? ReplaceFiles(string? folderToInstall, string folderRepository, int os, string? folderToPreserve)
    {
        string[] zips = Directory.GetFiles(folderRepository, "*.zip");
        if (zips.Length == 0)
            return "The source file to be installed was not found";

        if (string.IsNullOrWhiteSpace(folderToInstall) && zips.Length > 1)
            return "Multiple zip files in repository and no install folder specified";

        foreach (var zipPath in zips)
        {
            string folder = string.IsNullOrWhiteSpace(folderToInstall)
                ? Path.Combine(Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "", Path.GetFileNameWithoutExtension(zipPath))
                : folderToInstall;

            string? error = AtomicReplace(zipPath, folder, folderRepository, os, folderToPreserve);
            if (!string.IsNullOrWhiteSpace(error))
                return error;
        }
        return null;
    }

    /// <summary>
    /// Replaces the contents of <paramref name="folder"/> with the contents of
    /// <paramref name="zipPath"/> atomically. If anything fails, the previous
    /// state is restored from on-disk backups. The <paramref name="folderToPreserve"/>
    /// (typically the running updater's own folder, if it lives inside <paramref name="folder"/>)
    /// and the repository folder are preserved untouched.
    /// </summary>
    private static string? AtomicReplace(string zipPath, string folder, string folderRepository, int os, string? folderToPreserve)
    {
        string staging = Path.Combine(folderRepository, "staging-" + Guid.NewGuid().ToString("N"));

        var backupFiles = new List<(string original, string backup)>();
        var backupDirs = new List<(string original, string backup)>();
        var newFiles = new List<string>();
        var newDirs = new List<string>();

        try
        {
            if (Directory.Exists(folder))
                CleanLeftoverBackups(folder);
            else
                FileMethods.CreatAllPath(folder);

            Console.Write("Unzipping the newest program files... ");
            Directory.CreateDirectory(staging);
            string? extractError = SafeExtract(zipPath, staging);
            if (!string.IsNullOrWhiteSpace(extractError))
            {
                Console.WriteLine();
                return extractError;
            }
            Console.WriteLine("done");

            string? validationError = ValidateStaging(staging);
            if (!string.IsNullOrWhiteSpace(validationError))
                return validationError;

            Console.Write("Backing up old program files... ");
            BackupExistingTopLevel(folder, folderToPreserve, folderRepository, backupFiles, backupDirs);
            Console.WriteLine("done");

            Console.Write("Installing new files... ");
            MoveStagingIntoFolder(staging, folder, newFiles, newDirs);
            Console.WriteLine("done");

            if (os != 0)
            {
                string? chmodError = Permission(Path.Combine(folder, Path.GetFileNameWithoutExtension(zipPath)));
                if (!string.IsNullOrWhiteSpace(chmodError))
                {
                    // Don't roll back: files are correctly in place. A failed chmod is recoverable
                    // by the user and rolling back would be more destructive than the chmod issue.
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("WARNING: " + chmodError);
                    Console.ResetColor();
                }
            }

            // Success — commit by deleting backups
            foreach (var (_, backup) in backupFiles)
                TryDeleteFile(backup);
            foreach (var (_, backup) in backupDirs)
                TryDeleteDirectory(backup);
            TryDeleteDirectory(staging);
            TryDeleteFile(zipPath);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Update is complete");
            Console.ResetColor();
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error during install — rolling back: {ex.Message}");
            Console.ResetColor();
            Rollback(newFiles, newDirs, backupFiles, backupDirs, staging);
            return $"Install failed and was rolled back: {ex.Message}";
        }
    }

    private static string? ValidateStaging(string staging)
    {
        if (Directory.GetFileSystemEntries(staging).Length == 0)
            return "Extracted package is empty";

        foreach (var reserved in ReservedTopLevelNames)
        {
            string path = Path.Combine(staging, reserved);
            if (Directory.Exists(path) || File.Exists(path))
                return $"Refusing to install: package contains a top-level '{reserved}' entry, which is reserved by the updater";
        }
        return null;
    }

    private static void BackupExistingTopLevel(
        string folder,
        string? folderToPreserve,
        string folderRepository,
        List<(string original, string backup)> backupFiles,
        List<(string original, string backup)> backupDirs)
    {
        foreach (var file in Directory.GetFiles(folder))
        {
            if (file.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase))
                continue;
            string backup = file + BackupSuffix;
            File.Move(file, backup);
            backupFiles.Add((file, backup));
        }

        string? preserveFull = string.IsNullOrWhiteSpace(folderToPreserve) ? null : Path.GetFullPath(folderToPreserve);
        string repoFull = Path.GetFullPath(folderRepository);

        foreach (var dir in Directory.GetDirectories(folder))
        {
            string dirFull = Path.GetFullPath(dir);
            if (preserveFull is not null && string.Equals(dirFull, preserveFull, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(dirFull, repoFull, StringComparison.OrdinalIgnoreCase))
                continue;
            if (dir.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase))
                continue;
            string backup = dir + BackupSuffix;
            Directory.Move(dir, backup);
            backupDirs.Add((dir, backup));
        }
    }

    private static void MoveStagingIntoFolder(
        string staging,
        string folder,
        List<string> newFiles,
        List<string> newDirs)
    {
        foreach (var file in Directory.GetFiles(staging))
        {
            string target = Path.Combine(folder, Path.GetFileName(file));
            File.Move(file, target);
            newFiles.Add(target);
        }
        foreach (var dir in Directory.GetDirectories(staging))
        {
            string target = Path.Combine(folder, Path.GetFileName(dir));
            Directory.Move(dir, target);
            newDirs.Add(target);
        }
    }

    private static void Rollback(
        List<string> newFiles,
        List<string> newDirs,
        List<(string original, string backup)> backupFiles,
        List<(string original, string backup)> backupDirs,
        string staging)
    {
        foreach (var f in newFiles)
            TryDeleteFile(f);
        foreach (var d in newDirs)
            TryDeleteDirectory(d);

        foreach (var (orig, backup) in backupFiles)
        {
            try
            {
                if (File.Exists(backup))
                    File.Move(backup, orig);
            }
            catch { /* best-effort */ }
        }
        foreach (var (orig, backup) in backupDirs)
        {
            try
            {
                if (Directory.Exists(backup))
                    Directory.Move(backup, orig);
            }
            catch { /* best-effort */ }
        }

        TryDeleteDirectory(staging);
    }

    private static void CleanLeftoverBackups(string folder)
    {
        foreach (var file in Directory.GetFiles(folder))
        {
            if (file.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase))
                TryDeleteFile(file);
        }
        foreach (var dir in Directory.GetDirectories(folder))
        {
            if (dir.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase))
                TryDeleteDirectory(dir);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort */ }
    }

    private static string? SafeExtract(string zipPath, string destinationFolder)
    {
        string fullDestination = Path.GetFullPath(destinationFolder);
        if (!fullDestination.EndsWith(Path.DirectorySeparatorChar))
            fullDestination += Path.DirectorySeparatorChar;

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string targetPath = Path.GetFullPath(Path.Combine(fullDestination, entry.FullName));
                if (!targetPath.StartsWith(fullDestination, StringComparison.OrdinalIgnoreCase))
                    return $"Zip entry escapes destination folder (Zip Slip): '{entry.FullName}'";

                bool isDirectory = string.IsNullOrEmpty(entry.Name);
                if (isDirectory)
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                string? targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                entry.ExtractToFile(targetPath, overwrite: true);
            }
        }
        catch (Exception e)
        {
            return $"Problem trying to unzip file. Try to do it manually. Path file: {zipPath}{Environment.NewLine}{e.Message}";
        }

        return null;
    }

    public static int CheckOS()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return 0;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return 1;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return 2;
        return -1;
    }

    private static string? Permission(string fileExecute)
    {
        if (!ConsoleUtility.ExecCommandLine("chmod", " 700 " + fileExecute, null!, false, false, false, false))
        {
            return "An error was returned while trying to grant execute permission. " +
                   "Try to do it manually before continuing the upgrade. Path file: " + fileExecute;
        }
        return null;
    }
}
