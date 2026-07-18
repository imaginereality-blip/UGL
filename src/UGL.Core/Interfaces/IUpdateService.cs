using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Checks GitHub Releases for a newer version and applies a portable-ZIP update in
/// place. Implemented by GitHubUpdateService in UGL.Updates.
/// </summary>
public interface IUpdateService
{
    /// <summary>The app's own current version, read from the running assembly.</summary>
    string CurrentVersion { get; }

    /// <summary>Checks GitHub Releases for a newer version. Never throws for ordinary
    /// failure cases (no network, repo not found, no releases yet) — returns a result
    /// with IsUpdateAvailable = false and logs the reason instead, since a failed
    /// background check should never be disruptive.</summary>
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads and applies the update described by a prior CheckForUpdateAsync
    /// result. Only overwrites known application files/folders — user data (roms,
    /// bios, bezels, media, config, emulators, addons, retroarch) is never touched.
    /// Reports human-readable progress messages as it goes.
    /// </summary>
    Task ApplyUpdateAsync(UpdateCheckResult update, IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>Raised after a background check (see UGL.App's startup auto-check)
    /// finds an update — not raised for a manual check, since that already has its
    /// own direct result to show the user.</summary>
    event Action<UpdateCheckResult>? UpdateAvailable;

    /// <summary>Kicks off a background check without blocking the caller. Safe to
    /// call from startup — any failure is logged, never surfaced as an exception.</summary>
    void CheckForUpdateInBackground();
}
