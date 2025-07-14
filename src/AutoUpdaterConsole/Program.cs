// See https://aka.ms/new-console-template for more information
//AutoUpdaterConsole


using AutoUpdaterModel;
using BaseLibrary;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

CultureInfo culture = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = culture;
Thread.CurrentThread.CurrentUICulture = culture;

CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

//windows, linux, macos
int os = Services.CheckOS();

Version versionOld, versionNew;
string urlToDownload, folderToInstall, emailToReportIssue, nameProgram;
bool isFirst = true;
string error;

//Processa argumentos
if (args.Length == 6)
{
    error = Services.ProcessArg(args, out versionOld, out versionNew, out urlToDownload, out folderToInstall, out emailToReportIssue, out nameProgram);
    if (string.IsNullOrWhiteSpace(error))
    {
        ProcessError(error);
        return;
    }
    //Verifica conexão com internet
    if (!HTTPMethods.IsConnectedToInternetPing())
    {
        ProcessError("The computer don't have acess to internet");
        return;
    }
}
else
{
    versionOld = versionNew = null;
    urlToDownload = folderToInstall = emailToReportIssue = nameProgram = null;
}

Console.WriteLine($"Updating the {nameProgram} from {versionOld} to {versionNew} version");

string folderRepository = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
folderRepository = Path.Combine(folderRepository, "Repository");
if (!Directory.Exists(folderRepository))
{
    Directory.CreateDirectory(folderRepository);
}
if (!string.IsNullOrWhiteSpace(urlToDownload))
{
    string fileNameDownloaded = Path.Combine(folderRepository, nameProgram + ".zip");
    if (File.Exists(fileNameDownloaded))
    {
        File.Delete(fileNameDownloaded);
    }
    DownloadNewVersion(urlToDownload, fileNameDownloaded);
}

//Atualiza os arquivos
error = Services.ReplaceFiles(folderToInstall, folderRepository, os);

if (!string.IsNullOrWhiteSpace(error))
{
    ProcessError(error);
    return;
}







#region Methods
void DownloadNewVersion(string downloadFileUrl, string fileNameDownloaded)
{
    // = Path.GetFullPath("file.zip");
    Console.WriteLine("Downloading files...");
    if (!isFirst)
        isFirst = true;



    using (var client = new HttpClientDownloadWithProgress(downloadFileUrl, fileNameDownloaded))
    {

        client.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
        {
            if (isFirst)
            {
                isFirst = false;
                Console.WriteLine($"Total size: {totalFileSize / 100000} MB");
                ConsoleUtility.WriteProgressBar(0);
            }
            downloadProgressChanged(progressPercentage is not null ? (int)progressPercentage : 0);
        };
        client.StartDownload();
        downloadFileCompleted();
    }
}

void downloadFileCompleted()
{
    Console.WriteLine(" Done");
}
void downloadProgressChanged(int progressPercentage)
{
    ConsoleUtility.WriteProgressBar(progressPercentage, true);
}
void ProcessError(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("A error was found, the instalation/update will be aborted");
    Console.ResetColor();
    Console.WriteLine("Error message:");
    Console.WriteLine(message);

    if (!string.IsNullOrWhiteSpace(emailToReportIssue))
        ExceptionMethods.SendException(emailToReportIssue, new Exception(message), false, null);
}
#endregion
