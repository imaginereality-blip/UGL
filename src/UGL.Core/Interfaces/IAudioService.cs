using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Controls background music playback and navigation sound effects.
/// Implemented by LibVlcAudioService in UGL.Media.
/// All methods are safe to call from any thread.
/// </summary>
public interface IAudioService : IDisposable
{
    // ── Background music ───────────────────────────────────────────────────

    /// <summary>Loads the playlist and begins playback if EnableBackgroundMusic is true.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Switches to the playlist for the given category, or back to global if no override exists.</summary>
    Task SwitchPlaylistAsync(string categoryId, CancellationToken ct = default);

    /// <summary>
    /// Manually cycles to the next (direction > 0) or previous (direction &lt; 0)
    /// playlist among all configured playlists that have at least one track,
    /// independent of the currently browsed category. Raises PlaylistChanged with
    /// the new playlist's display name — intended for a brief on-screen indicator,
    /// not raised for the automatic category-based switching SwitchPlaylistAsync does.
    /// </summary>
    Task CyclePlaylistAsync(int direction, CancellationToken ct = default);

    /// <summary>Raised after a manual CyclePlaylistAsync switch, with the new
    /// playlist's display name.</summary>
    event Action<string>? PlaylistChanged;

    /// <summary>Pauses background music (e.g. while settings overlay is open).</summary>
    void Pause();

    /// <summary>Resumes background music after a pause.</summary>
    void Resume();

    /// <summary>Stops playback and releases all media resources.</summary>
    void Stop();

    float MusicVolume { get; set; }
    bool IsMusicEnabled { get; set; }

    // ── Navigation sounds ──────────────────────────────────────────────────

    void PlayNavigate();
    void PlayConfirm();
    void PlayBack();
    void PlayError();

    float SoundVolume { get; set; }
    bool IsSoundEnabled { get; set; }
}
