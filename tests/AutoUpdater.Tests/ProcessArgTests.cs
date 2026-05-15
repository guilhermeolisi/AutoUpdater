using AutoUpdaterModel;
using Xunit;

namespace AutoUpdater.Tests;

public class ProcessArgTests
{
    [Fact]
    public void Empty_args_returns_no_argument()
    {
        string err = Services.ProcessArg(Array.Empty<string>(),
            out _, out _, out _, out _, out _, out _, out _);
        Assert.Equal("no argument", err);
    }

    [Fact]
    public void Wrong_count_returns_descriptive_error()
    {
        string err = Services.ProcessArg(new[] { "1", "2", "3" },
            out _, out _, out _, out _, out _, out _, out _);
        Assert.Contains("7 arguments", err);
    }

    [Fact]
    public void Invalid_old_version_is_rejected()
    {
        string err = Services.ProcessArg(
            new[] { "not-a-version", "1.0", "url", "folder", "email", "name", "0" },
            out _, out _, out _, out _, out _, out _, out _);
        Assert.Contains("Old version", err);
    }

    [Fact]
    public void Invalid_new_version_is_rejected()
    {
        string err = Services.ProcessArg(
            new[] { "1.0", "abc", "url", "folder", "email", "name", "0" },
            out _, out _, out _, out _, out _, out _, out _);
        Assert.Contains("New version", err);
    }

    [Fact]
    public void Negative_pid_is_rejected()
    {
        string err = Services.ProcessArg(
            new[] { "1.0", "1.1", "url", "folder", "email", "name", "-1" },
            out _, out _, out _, out _, out _, out _, out _);
        Assert.Contains("Caller process id", err);
    }

    [Fact]
    public void Non_numeric_pid_is_rejected()
    {
        string err = Services.ProcessArg(
            new[] { "1.0", "1.1", "url", "folder", "email", "name", "abc" },
            out _, out _, out _, out _, out _, out _, out _);
        Assert.Contains("Caller process id", err);
    }

    [Fact]
    public void Valid_args_parse_correctly()
    {
        string err = Services.ProcessArg(
            new[] { "1.4.2", "1.5.0", "https://example.com/v.json", @"C:\app", "support@x.com", "MyApp", "1234" },
            out var oldV, out var newV, out var url, out var folder, out var email, out var name, out var pid);

        Assert.Null(err);
        Assert.Equal(new Version(1, 4, 2), oldV);
        Assert.Equal(new Version(1, 5, 0), newV);
        Assert.Equal("https://example.com/v.json", url);
        Assert.Equal(@"C:\app", folder);
        Assert.Equal("support@x.com", email);
        Assert.Equal("MyApp", name);
        Assert.Equal(1234, pid);
    }
}

public class OsKeyTests
{
    [Theory]
    [InlineData(0, "windows")]
    [InlineData(1, "linux")]
    [InlineData(2, "macos")]
    public void FromIndex_maps_known_values(int index, string expected)
    {
        Assert.Equal(expected, OsKey.FromIndex(index));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void FromIndex_throws_for_unknown_values(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OsKey.FromIndex(index));
    }
}
