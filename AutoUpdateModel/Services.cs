using BaseLibrary;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AutoUpdaterModel;

public static class Services
{
    public static string ProcessArg(string[] args, out Version versionOld, out Version versionNew, out string urlToDownload, out string folderToInstall, out string emailToReportIssue, out string nameProgram)
    {

        if (args.Length == 0)
        {
            versionOld = versionNew = null;
            urlToDownload = folderToInstall = emailToReportIssue = nameProgram = null;
            return "no argument";
        }
        else if (args.Length != 7)
        {
            versionOld = versionNew = null;
            urlToDownload =  folderToInstall = emailToReportIssue = nameProgram = null;
            return "It is necessary three arguments: old Version; new Version; url; folder to install; emai to report issue; url to verify AutoUpdater Version; name of program";
        }

        urlToDownload = args[2];
        folderToInstall = args[3];
        emailToReportIssue = args[4];
        nameProgram = args[5];

        if (!Version.TryParse(args[0], out versionOld))
        {

        }
        if (!Version.TryParse(args[1], out versionNew))
        {

        }

        return null;
    }
    public static string ReplaceFiles(string folderToInstall, string folderRepository, int os)
    {
        string[] filesToInstall;


        filesToInstall = Directory.GetFiles(folderRepository, "*.zip");
        if (filesToInstall.Length == 0)
        {
            return "The source file to be installed was not found";
        }
        if (string.IsNullOrWhiteSpace(folderToInstall) && filesToInstall.Length > 1)
        {
            //TODO retorna um erro
            return "";
        }
        foreach (var file in filesToInstall)
        {
            string folder;
            if (string.IsNullOrWhiteSpace(folderToInstall))
            {
                folder = Path.Combine(Path.GetPathRoot(Directory.GetCurrentDirectory()), Path.GetFileNameWithoutExtension(file));
            }
            else
            {
                folder = folderToInstall;
            }

            //Verifica se existe a pasta
            if (Directory.Exists(folder))
            {
                Console.Write("Deleting the old program files... ");
                string[] filesToDelete = Directory.GetFiles(folder);

                foreach (var item in filesToDelete)
                {
                    try
                    {
                        File.Delete(item);
                    }
                    catch (Exception ex)
                    {
                        return ex.ToString();
                    }
                }
                string[] directoriesToDelete = Directory.GetDirectories(folder);
                string folderAutoUpdater = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                foreach (var item in directoriesToDelete)
                {
                    if (item == folderAutoUpdater)
                        continue;
                    try
                    {
                        Directory.Delete(item, true);
                    }
                    catch (Exception ex)
                    {
                        return ex.ToString();
                    }
                }
                Console.WriteLine("done");
            }
            else
            {
                FileMethods.CreatAllPath(folder);
            }
            try
            {

                Console.Write("Unzipping the newest program files... ");
                try
                {
                    ZipFile.ExtractToDirectory(Path.Combine(file), folder);
                }
                catch (Exception e)
                {
                    string error = "Problem trying unzip file. Try to do it manually. Path file: " + file + Environment.NewLine + e.Message;
                    return error;
                }
                if (File.Exists(file))
                    File.Delete(file);
                Console.WriteLine("done");


            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
            if (os != 0)
            {
                string error = Permission(Path.Combine(folder, Path.GetFileNameWithoutExtension(file)));
                if (string.IsNullOrWhiteSpace(error))
                {
                    return error;
                }
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Sindarin update is complete");
            Console.ResetColor();
        }
        return null;
    }
    
    public static int CheckOS()
    {
        //windows, linux, macos
        int os = -1;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            os = 0;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            os = 1;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            os = 2;
        }
        return os;
    }

    private static string Permission(string fileExecute)
    {

        if (!ConsoleUtility.ExecCommandLine("chmod", " 700 " + fileExecute, false, false, false, false))
        {
            string message = "An error was returned while trying to give the SindarinInstaller execute permission. try to do it manually before continuing the upgrade. Path file: " + fileExecute;
            return message;
        }
        return null;
    }

}