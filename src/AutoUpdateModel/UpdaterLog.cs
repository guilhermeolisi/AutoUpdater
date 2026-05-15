using System.Globalization;

namespace AutoUpdaterModel;

/// <summary>
/// Append-only file logger for the AutoUpdater. One log per program, located at
/// %LOCALAPPDATA%\&lt;programName&gt;\updater.log on Windows or
/// $XDG_DATA_HOME (or ~/.local/share)/&lt;programName&gt;/updater.log on Unix.
/// Rotates once when the file passes 1 MB.
/// </summary>
public static class UpdaterLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object _lock = new();
    private static string? _logPath;

    public static string? LogPath => _logPath;

    /// <summary>
    /// Initialises the logger. Safe to call multiple times — last call wins.
    /// </summary>
    public static void Init(string? programName)
    {
        try
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.GetTempPath();

            string sub = string.IsNullOrWhiteSpace(programName) ? "AutoUpdater" : programName;
            string logDir = Path.Combine(baseDir, sub);
            Directory.CreateDirectory(logDir);

            string path = Path.Combine(logDir, "updater.log");
            RotateIfLarge(path);
            _logPath = path;
        }
        catch
        {
            // Logger never throws — fall back to disabled state.
            _logPath = null;
        }
    }

    public static void Info(string message)             => Write("INFO", message, null);
    public static void Warn(string message)             => Write("WARN", message, null);
    public static void Error(string message)            => Write("ERROR", message, null);
    public static void Error(string message, Exception ex) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        if (_logPath is null) return;

        try
        {
            string line = ex is null
                ? $"{DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}"
                : $"{DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)} [{level}] {message} | {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";

            lock (_lock)
            {
                File.AppendAllText(_logPath, line);
            }
        }
        catch
        {
            // Never throw from the logger.
        }
    }

    private static void RotateIfLarge(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length <= MaxBytes) return;

            string old = path + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(path, old);
        }
        catch
        {
            // Non-fatal — keep going.
        }
    }
}
