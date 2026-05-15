using AutoUpdaterModel;
using Xunit;

namespace AutoUpdater.Tests;

public class ManifestTests
{
    [Fact]
    public void Round_trip_preserves_all_fields()
    {
        var original = new VersionManifest
        {
            Version = "1.4.2",
            MinimumVersion = "1.0.0",
            Artifacts = new Dictionary<string, ArtifactInfo>
            {
                ["windows"] = new() { Url = "https://x/win.zip", Sha256 = "ABC", Signature = "sig1" },
                ["linux"]   = new() { Url = "https://x/lnx.zip", Sha256 = "DEF", Signature = "sig2" },
            }
        };

        string json = original.Serialize();
        VersionManifest parsed = VersionManifest.Parse(json);

        Assert.Equal("1.4.2", parsed.Version);
        Assert.Equal("1.0.0", parsed.MinimumVersion);
        Assert.Equal(2, parsed.Artifacts.Count);
        Assert.Equal("https://x/win.zip", parsed.Artifacts["windows"].Url);
        Assert.Equal("ABC", parsed.Artifacts["windows"].Sha256);
        Assert.Equal("sig1", parsed.Artifacts["windows"].Signature);
    }

    [Fact]
    public void Missing_minimumVersion_parses_as_empty()
    {
        const string json = """
        { "version": "1.0.0", "artifacts": { "windows": { "url": "u", "sha256": "s", "signature": "x" } } }
        """;
        var parsed = VersionManifest.Parse(json);
        Assert.Equal(string.Empty, parsed.MinimumVersion);
    }

    [Fact]
    public void Case_insensitive_property_names_supported()
    {
        const string json = """
        { "Version": "2.0", "Artifacts": { "windows": { "URL": "u", "SHA256": "s", "Signature": "x" } } }
        """;
        var parsed = VersionManifest.Parse(json);
        Assert.Equal("2.0", parsed.Version);
        Assert.Equal("u", parsed.Artifacts["windows"].Url);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        const string json = """
        {
          // top-level comment
          "version": "1.0",
          "artifacts": {
            "windows": { "url": "u", "sha256": "s", "signature": "x", },
          },
        }
        """;
        var parsed = VersionManifest.Parse(json);
        Assert.Equal("1.0", parsed.Version);
    }
}

public class UpdateCheckResultTests
{
    [Fact]
    public void HasUpdate_true_when_NewVersion_set()
    {
        var r = new UpdateCheckResult { NewVersion = new Version(1, 1, 0), CurrentVersion = new Version(1, 0, 0) };
        Assert.True(r.HasUpdate);
    }

    [Fact]
    public void HasUpdate_false_when_NewVersion_null()
    {
        var r = new UpdateCheckResult { CurrentVersion = new Version(1, 0, 0) };
        Assert.False(r.HasUpdate);
    }

    [Fact]
    public void IsCurrentBelowMinimum_true_when_below()
    {
        var r = new UpdateCheckResult
        {
            CurrentVersion = new Version(1, 0, 0),
            MinimumVersion = new Version(1, 5, 0)
        };
        Assert.True(r.IsCurrentBelowMinimum);
    }

    [Fact]
    public void IsCurrentBelowMinimum_false_when_at_or_above()
    {
        var r = new UpdateCheckResult
        {
            CurrentVersion = new Version(1, 5, 0),
            MinimumVersion = new Version(1, 5, 0)
        };
        Assert.False(r.IsCurrentBelowMinimum);
    }

    [Fact]
    public void IsCurrentBelowMinimum_false_when_minimum_unset()
    {
        var r = new UpdateCheckResult { CurrentVersion = new Version(0, 1, 0) };
        Assert.False(r.IsCurrentBelowMinimum);
    }
}
