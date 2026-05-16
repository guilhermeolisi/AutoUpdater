using System.Runtime.InteropServices;
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
        => JsonSerializer.Deserialize(json, ManifestJsonContext.Default.VersionManifest)
           ?? new VersionManifest();

    public string Serialize()
        => JsonSerializer.Serialize(this, ManifestJsonContext.Default.VersionManifest);
}

// Source generator: serialização/desserialização sem reflexão, compatível com
// trimming e Native AOT. As opções de leitura/escrita ficam no contexto.
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(VersionManifest))]
internal partial class ManifestJsonContext : JsonSerializerContext;

public sealed class ArtifactInfo
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";
}

/// <summary>
/// Chaves de artefato no manifesto. São compostas por SO e arquitetura
/// (ex.: "windows-x64", "windows-arm64", "linux-x64"), permitindo distribuir
/// builds distintos por arquitetura no mesmo manifesto.
/// </summary>
public static class OsKey
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string MacOS = "macos";

    public static readonly string[] ValidOs = { Windows, Linux, MacOS };
    public static readonly string[] ValidArch = { "x86", "x64", "arm64", "arm" };

    /// <summary>Parte de SO da chave. String vazia se o SO não for suportado.</summary>
    public static string CurrentOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return MacOS;
        return "";
    }

    /// <summary>
    /// Arquitetura do processo atual. Usamos ProcessArchitecture (não a do SO)
    /// para que a atualização troque o binário pela MESMA arquitetura em execução
    /// — inclusive sob emulação (ex.: build x64 rodando em Windows ARM64).
    /// </summary>
    public static string CurrentArch() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        var other => other.ToString().ToLowerInvariant()
    };

    /// <summary>Chave composta SO-arquitetura do runtime atual (ex.: "windows-x64").</summary>
    public static string Current() => Build(CurrentOs(), CurrentArch());

    public static string Build(string os, string arch) => $"{os}-{arch}";

    /// <summary>Valida uma chave no formato "&lt;so&gt;-&lt;arch&gt;".</summary>
    public static bool IsValidKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        int dash = key.IndexOf('-');
        if (dash <= 0 || dash >= key.Length - 1) return false;
        string os = key[..dash];
        string arch = key[(dash + 1)..];
        return Array.IndexOf(ValidOs, os) >= 0 && Array.IndexOf(ValidArch, arch) >= 0;
    }
}
