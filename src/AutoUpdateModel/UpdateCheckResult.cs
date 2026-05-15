namespace AutoUpdaterModel;

/// <summary>
/// Result of an update check. Inspect <see cref="Message"/> first — if non-null,
/// the check itself failed (offline, manifest invalid, etc.) and other fields
/// are not meaningful.
/// </summary>
public sealed class UpdateCheckResult
{
    /// <summary>Newer version available, or null if you're already up-to-date.</summary>
    public Version? NewVersion { get; init; }

    /// <summary>The version currently running (assembly version of the entry assembly).</summary>
    public Version? CurrentVersion { get; init; }

    /// <summary>
    /// Minimum version the server demands clients to run. Null if the manifest
    /// did not specify one. Useful to phase out vulnerable releases.
    /// </summary>
    public Version? MinimumVersion { get; init; }

    /// <summary>Error message, or null if the check succeeded.</summary>
    public string? Message { get; init; }

    public bool HasUpdate => NewVersion is not null;

    public bool IsCurrentBelowMinimum =>
        MinimumVersion is not null && CurrentVersion is not null && CurrentVersion < MinimumVersion;
}
