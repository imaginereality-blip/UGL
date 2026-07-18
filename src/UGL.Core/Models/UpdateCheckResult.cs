namespace UGL.Core.Models;

/// <summary>
/// Result of checking GitHub Releases for a newer version than the one currently
/// running. Also carries what's needed to actually apply the update if the user
/// confirms — the download URL and enough metadata to show them what they'd be
/// getting.
/// </summary>
public sealed class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseNotesUrl { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;

    /// <summary>Direct download URL for the portable ZIP asset. Empty if no update is
    /// available, or if the latest release doesn't have a recognizable ZIP asset.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    public long DownloadSizeBytes { get; set; }
}
