using System.IO.Compression;
using AutoUpdaterModel;
using Xunit;

namespace AutoUpdater.Tests;

/// <summary>
/// End-to-end tests for the atomic install + rollback in
/// <see cref="Services.ReplaceFiles"/>.
/// </summary>
public class ServicesAtomicTests : IDisposable
{
    private readonly string _root;
    private readonly string _install;
    private readonly string _repository;
    private readonly string _zipSrc;
    private readonly string _zipPath;

    public ServicesAtomicTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "au-services-" + Guid.NewGuid().ToString("N"));
        _install = Path.Combine(_root, "install");
        _repository = Path.Combine(_root, "repo");
        _zipSrc = Path.Combine(_root, "zipsrc");
        _zipPath = Path.Combine(_repository, "newpkg.zip");
        Directory.CreateDirectory(_install);
        Directory.CreateDirectory(_repository);
        Directory.CreateDirectory(_zipSrc);

        // Pre-existing app files
        File.WriteAllText(Path.Combine(_install, "app.exe"), "OLD app");
        File.WriteAllText(Path.Combine(_install, "config.ini"), "OLD config");
        Directory.CreateDirectory(Path.Combine(_install, "lib"));
        File.WriteAllText(Path.Combine(_install, "lib", "thing.dll"), "OLD lib");

        // Pre-existing AutoUpdater subfolder that must be preserved
        Directory.CreateDirectory(Path.Combine(_install, "AutoUpdater"));
        File.WriteAllText(Path.Combine(_install, "AutoUpdater", "AutoUpdaterConsole.dll"), "RUNNING");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void BuildZip()
    {
        ZipFile.CreateFromDirectory(_zipSrc, _zipPath);
    }

    private string PreserveFolder => Path.Combine(_install, "AutoUpdater");

    [Fact]
    public void Success_replaces_files_and_preserves_AutoUpdater()
    {
        File.WriteAllText(Path.Combine(_zipSrc, "app.exe"), "NEW app");
        File.WriteAllText(Path.Combine(_zipSrc, "newfile.txt"), "v2 added");
        Directory.CreateDirectory(Path.Combine(_zipSrc, "lib"));
        File.WriteAllText(Path.Combine(_zipSrc, "lib", "thing.dll"), "NEW lib");
        BuildZip();

        string err = Services.ReplaceFiles(_install, _repository, 0, PreserveFolder);

        Assert.Null(err);
        Assert.Equal("NEW app", File.ReadAllText(Path.Combine(_install, "app.exe")));
        Assert.True(File.Exists(Path.Combine(_install, "newfile.txt")));
        Assert.False(File.Exists(Path.Combine(_install, "config.ini"))); // not in new zip
        Assert.True(File.Exists(Path.Combine(_install, "AutoUpdater", "AutoUpdaterConsole.dll")));
        Assert.Empty(Directory.GetFileSystemEntries(_install, "*.update.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public void Rollback_restores_original_when_install_fails()
    {
        File.WriteAllText(Path.Combine(_zipSrc, "app.exe"), "NEW app");
        Directory.CreateDirectory(Path.Combine(_zipSrc, "lib"));
        File.WriteAllText(Path.Combine(_zipSrc, "lib", "thing.dll"), "NEW lib");
        BuildZip();

        string err;
        // Hold an open handle to a file inside install/lib so Directory.Move
        // ("lib" -> "lib.update.bak") fails during the backup phase.
        using (var locker = new FileStream(
            Path.Combine(_install, "lib", "thing.dll"),
            FileMode.Open, FileAccess.Read, FileShare.None))
        {
            err = Services.ReplaceFiles(_install, _repository, 0, PreserveFolder);
        }
        // Lock released here — safe to read files for assertions.

        Assert.NotNull(err);
        Assert.Contains("rolled back", err);
        Assert.Equal("OLD app", File.ReadAllText(Path.Combine(_install, "app.exe")));
        Assert.Equal("OLD config", File.ReadAllText(Path.Combine(_install, "config.ini")));
        Assert.Equal("OLD lib", File.ReadAllText(Path.Combine(_install, "lib", "thing.dll")));
        Assert.True(File.Exists(Path.Combine(_install, "AutoUpdater", "AutoUpdaterConsole.dll")));
        Assert.Empty(Directory.GetFileSystemEntries(_install, "*.update.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public void Reserved_top_level_AutoUpdater_in_zip_is_rejected()
    {
        Directory.CreateDirectory(Path.Combine(_zipSrc, "AutoUpdater"));
        File.WriteAllText(Path.Combine(_zipSrc, "AutoUpdater", "evil.exe"), "x");
        File.WriteAllText(Path.Combine(_zipSrc, "app.exe"), "NEW");
        BuildZip();

        string err = Services.ReplaceFiles(_install, _repository, 0, PreserveFolder);

        Assert.NotNull(err);
        Assert.Contains("reserved", err);
        Assert.Equal("OLD app", File.ReadAllText(Path.Combine(_install, "app.exe")));
        // Ensure the original AutoUpdater wasn't touched by the failed install
        Assert.Equal("RUNNING", File.ReadAllText(Path.Combine(_install, "AutoUpdater", "AutoUpdaterConsole.dll")));
    }

    [Fact]
    public void Reserved_top_level_Repository_in_zip_is_rejected()
    {
        Directory.CreateDirectory(Path.Combine(_zipSrc, "Repository"));
        File.WriteAllText(Path.Combine(_zipSrc, "Repository", "x.txt"), "x");
        File.WriteAllText(Path.Combine(_zipSrc, "app.exe"), "NEW");
        BuildZip();

        string err = Services.ReplaceFiles(_install, _repository, 0, PreserveFolder);

        Assert.NotNull(err);
        Assert.Contains("reserved", err);
    }

    [Fact]
    public void Leftover_backup_files_are_cleaned_before_install()
    {
        File.WriteAllText(Path.Combine(_install, "stale.update.bak"), "leftover");
        File.WriteAllText(Path.Combine(_zipSrc, "app.exe"), "NEW");
        BuildZip();

        string err = Services.ReplaceFiles(_install, _repository, 0, PreserveFolder);

        Assert.Null(err);
        Assert.False(File.Exists(Path.Combine(_install, "stale.update.bak")));
        Assert.Equal("NEW", File.ReadAllText(Path.Combine(_install, "app.exe")));
    }

    [Fact]
    public void Empty_repository_returns_friendly_error()
    {
        string err = Services.ReplaceFiles(_install, _repository, 0, PreserveFolder);
        Assert.NotNull(err);
        Assert.Contains("source file", err);
    }

    [Fact]
    public void Empty_zip_is_rejected()
    {
        BuildZip(); // empty zipSrc -> empty zip
        string err = Services.ReplaceFiles(_install, _repository, 0, PreserveFolder);
        Assert.NotNull(err);
        Assert.Contains("empty", err);
    }
}

public class ZipSlipTests
{
    [Fact]
    public void Zip_with_path_traversal_is_rejected()
    {
        string root = Path.Combine(Path.GetTempPath(), "zipslip-" + Guid.NewGuid().ToString("N"));
        string install = Path.Combine(root, "install");
        string repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(repo);

        try
        {
            string zipPath = Path.Combine(repo, "evil.zip");
            using (var fs = File.Create(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("..\\..\\escaped.txt");
                using var ws = entry.Open();
                ws.Write("pwned"u8.ToArray());
            }

            string err = Services.ReplaceFiles(install, repo, 0, folderToPreserve: null);
            Assert.NotNull(err);
            Assert.Contains("Zip Slip", err);

            string escaped = Path.Combine(Path.GetDirectoryName(install)!, "..", "escaped.txt");
            Assert.False(File.Exists(Path.GetFullPath(escaped)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
