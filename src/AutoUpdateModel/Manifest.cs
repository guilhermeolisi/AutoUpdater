using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoUpdaterModel;

public sealed class VersionManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>
    /// Optional. If set, clients running a version below this should not run
    /// (or should be forced to update). Empty string means "no minimum".
    /// </summary>
    [JsonPropertyName("minimumVersion")]
    public string MinimumVersion { get; set; } = "";

    [JsonPropertyName("artifacts")]
    public Dictionary<string, ArtifactInfo> Artifacts { get; set; } = new();

    public static VersionManifest Parse(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        return JsonSerializer.Deserialize<VersionManifest>(json, options) ?? new VersionManifest();
    }

    public string Serialize()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };
        return JsonSerializer.Serialize(this, options);
    }
}

public sealed class ArtifactInfo
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";
}

public static class OsKey
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string MacOS = "macos";

    public static string FromIndex(int os) => os switch
    {
        0 => Windows,
        1 => Linux,
        2 => MacOS,
        _ => throw new ArgumentOutOfRangeException(nameof(os), $"Unknown OS index: {os}")
    };
}
