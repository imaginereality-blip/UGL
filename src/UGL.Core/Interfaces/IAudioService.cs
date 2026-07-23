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
    /// playlist. The cyclable pool is Global playlist(s) plus whichever playlist(s)
    /// are assigned to currentCategoryId (null if not currently browsing a specific
    /// category, e.g. the Home Menu with nothing selected yet) — other categories'
    /// playlists are deliberately excluded, since they're "locked" to their own
    /// category rather than available everywhere. Raises PlaylistChanged with the
    /// new playlist's display name — intended for a brief on-screen indicator, not
    /// raised for the automatic category-based switching SwitchPlaylistAsync does.
    /// </summary>
    Task CyclePlaylistAsync(int direction, string? currentCategoryId, CancellationToken ct = default);

    /// <summary>Raised after a manual CyclePlaylistAsync switch, with the new
    /// playlist's display name.</summary>
    event Action<string>? PlaylistChanged;

    /// <summary>Raised whenever the currently playing track changes — a manual
    /// skip, an automatic advance to the next track, or a shuffle wrap-around —
    /// with the new track's display name. Deliberately NOT raised for the first
    /// track of a playlist that just started via PlaylistChanged (switching
    /// playlists, or the initial track on startup), since that's already
    /// announced there and firing both back-to-back would just have this
    /// overwrite that one's toast before it's readable.</summary>
    event Action<string>? TrackChanged;

    /// <summary>Skips to the next track in the currently playing playlist.
    /// No-op if nothing is currently playing.</summary>
    void SkipToNextTrack();

    /// <summary>Skips to the previous track in the currently playing playlist.
    /// No-op if nothing is currently playing.</summary>
    void SkipToPreviousTrack();

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
