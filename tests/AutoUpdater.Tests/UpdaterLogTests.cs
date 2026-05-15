using AutoUpdaterModel;
using Xunit;

namespace AutoUpdater.Tests;

public class UpdaterLogTests
{
    [Fact]
    public void Init_creates_log_file_under_localappdata()
    {
        string testName = "AutoUpdaterTests-" + Guid.NewGuid().ToString("N");
        try
        {
            UpdaterLog.Init(testName);
            UpdaterLog.Info("hello");

            Assert.NotNull(UpdaterLog.LogPath);
            Assert.True(File.Exists(UpdaterLog.LogPath));
            string contents = File.ReadAllText(UpdaterLog.LogPath);
            Assert.Contains("[INFO] hello", contents);
        }
        finally
        {
            CleanupTestLog(testName);
        }
    }

    [Fact]
    public void Levels_are_distinguishable_in_output()
    {
        string testName = "AutoUpdaterTests-" + Guid.NewGuid().ToString("N");
        try
        {
            UpdaterLog.Init(testName);
            UpdaterLog.Info("info-line");
            UpdaterLog.Warn("warn-line");
            UpdaterLog.Error("error-line");

            string contents = File.ReadAllText(UpdaterLog.LogPath!);
            Assert.Contains("[INFO] info-line", contents);
            Assert.Contains("[WARN] warn-line", contents);
            Assert.Contains("[ERROR] error-line", contents);
        }
        finally
        {
            CleanupTestLog(testName);
        }
    }

    [Fact]
    public void Exception_overload_includes_type_message_and_stack()
    {
        string testName = "AutoUpdaterTests-" + Guid.NewGuid().ToString("N");
        try
        {
            UpdaterLog.Init(testName);
            try { throw new InvalidOperationException("kaboom"); }
            catch (Exception ex) { UpdaterLog.Error("ctx", ex); }

            string contents = File.ReadAllText(UpdaterLog.LogPath!);
            Assert.Contains("ctx", contents);
            Assert.Contains("InvalidOperationException", contents);
            Assert.Contains("kaboom", contents);
        }
        finally
        {
            CleanupTestLog(testName);
        }
    }

    [Fact]
    public void Init_with_null_or_empty_name_falls_back_to_default()
    {
        // Should not throw and should still produce a usable log path.
        UpdaterLog.Init(null!);
        Assert.NotNull(UpdaterLog.LogPath);
    }

    private static void CleanupTestLog(string testName)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                testName);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }
}
