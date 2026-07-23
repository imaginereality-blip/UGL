using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;
using LibVlcCore = LibVLCSharp.Shared.Core;
using LibVlcMedia = LibVLCSharp.Shared.Media;

namespace UGL.Media;

/// <summary>
/// Production IAudioService implementation using LibVLCSharp.
///
/// Background music:
///   - Loads tracks from AudioPlaylist via IAudioPlaylistRepository
///   - Supports shuffle and per-category playlist overrides
///   - Crossfades between playlists by stopping current and starting next
///
/// Navigation sounds:
///   - Plays short WAV/OGG files from {exe}/media/sounds/
///   - Each sound uses its own short-lived MediaPlayer to avoid blocking music
///   - Missing sound files are silently skipped
///
/// LibVLC native DLLs are bundled via VideoLAN.LibVLC.Windows NuGet package —
/// no VLC installation required on the cabinet machine.
/// </summary>
public sealed class LibVlcAudioService : IAudioService
{
    private readonly IAudioPlaylistRepository _playlistRepo;
    private readonly IAudioTrackRepository _trackRepo;
    private readonly IConfigurationService _config;
    private readonly ILogger<LibVlcAudioService> _logger;

    private LibVLC? _vlc;
    private MediaPlayer? _musicPlayer;
    private readonly SemaphoreSlim _musicLock = new(1, 1);

    /// <summary>
    /// Exposes the shared LibVLC instance for reuse by LibVlcVideoPreviewService.
    /// Null until StartAsync completes successfully.
    /// </summary>
    public LibVLC? VlcInstance => _vlc;

    private AudioPlaylist? _currentPlaylist;
    private List<string> _shuffledTracks = [];
    private int _trackIndex;

    // Incremented every time PlayCurrentTrack starts a new media - used by
    // VerifyPlaybackStartedAsync to detect "has something newer already taken
    // over" without relying on reference-equality of the Media object itself
    // (LibVLCSharp may hand back a different managed wrapper on each read of
    // .Media even for the same underlying native resource).
    private int _playGeneration;

    // Master volume, stored separately from _musicPlayer.Volume rather than
    // wrapping it directly - the effective volume applied to the player is
    // _masterMusicVolume * (current playlist's own Volume), computed whenever a
    // track loads (see LoadAndPlayPlaylistAsync). Storing it separately means
    // setting one doesn't silently get overwritten by the other, which is what
    // was happening before: this used to read/write _musicPlayer.Volume directly,
    // and LoadAndPlayPlaylistAsync writing the playlist's own volume there
    // afterward would just clobber whatever master volume had been set.
    private float _masterMusicVolume = 0.5f;

    public float MusicVolume
    {
        get => _masterMusicVolume;
        set
        {
            _masterMusicVolume = value;
            ApplyEffectiveVolume();
        }
    }

    private void ApplyEffectiveVolume()
    {
        if (_musicPlayer is null) return;
        var playlistVolume = _currentPlaylist?.Volume ?? 1.0f;
        var effective = (int)(_masterMusicVolume * playlistVolume * 100);
        _musicPlayer.Volume = effective;
    }

    public bool IsMusicEnabled { get; set; } = true;
    public float SoundVolume   { get; set; } = 1.0f;
    public bool IsSoundEnabled { get; set; } = true;

    public LibVlcAudioService(
        IAudioPlaylistRepository playlistRepo,
        IAudioTrackRepository trackRepo,
        IConfigurationService config,
        ILogger<LibVlcAudioService> logger)
    {
        _playlistRepo = playlistRepo;
        _trackRepo = trackRepo;
        _config = config;
        _logger = logger;
    }

    // ── Startup ────────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct = default)
    {
        IsMusicEnabled  = _config.Settings.EnableBackgroundMusic;
        IsSoundEnabled  = _config.Settings.EnableNavigationSounds;

        try
        {
            LibVlcCore.Initialize();
            _vlc = new LibVLC(enableDebugLogs: false);
            _musicPlayer = new MediaPlayer(_vlc);
            _musicPlayer.EndReached += OnTrackEnded;

            // Apply the saved master volume immediately — this was the actual bug:
            // only IsMusicEnabled/IsSoundEnabled were ever read from settings here,
            // never the volume levels. LoadAndPlayPlaylistAsync below does set
            // _musicPlayer.Volume from the *playlist's own* volume field, which
            // masked this for anyone whose playlist volume happened to be
            // reasonable — but the actual saved master volume was never applied
            // until the user visited Settings -> Audio and hit Save, which is the
            // only other place that assigns MusicVolume/SoundVolume.
            MusicVolume = _config.Settings.MusicVolume;
            SoundVolume = _config.Settings.SoundVolume;

            _logger.LogInformation("LibVLC initialized.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LibVLC failed to initialize. Audio will be disabled.");
            IsMusicEnabled = false;
            IsSoundEnabled = false;
            return;
        }

        if (!IsMusicEnabled)
        {
            return;
        }

        // Load and start the global playlist
        var all = await _playlistRepo.GetAllAsync(ct);

        var global = all.FirstOrDefault(p => p.IsGlobal);

        if (global is not null && global.TrackIds.Count > 0)
        {
            await LoadAndPlayPlaylistAsync(global, ct);
        }
        else
        {
            _logger.LogInformation("No Global playlist configured (or it has no tracks) — no background music.");
        }
    }


    // ── Playlist switching ─────────────────────────────────────────────────

    public async Task SwitchPlaylistAsync(string categoryId, CancellationToken ct = default)
    {
        if (!IsMusicEnabled || _vlc is null) return;

        var all = await _playlistRepo.GetAllAsync(ct);
        var categoryPlaylist = all.FirstOrDefault(p =>
            p.CategoryIds.Any(id => string.Equals(id, categoryId, StringComparison.OrdinalIgnoreCase)));

        if (categoryPlaylist is not null && categoryPlaylist.TrackIds.Count > 0)
        {
            _logger.LogDebug("Switching to category playlist: {Name} (category: {Id})", categoryPlaylist.Name, categoryId);
            await LoadAndPlayPlaylistAsync(categoryPlaylist, ct);
        }
        else if (_currentPlaylist is null || !_currentPlaylist.IsGlobal)
        {
            // No category override — revert to global
            var global = all.FirstOrDefault(p => p.IsGlobal);
            if (global is not null && global.TrackIds.Count > 0)
            {
                _logger.LogDebug("Reverting to global playlist.");
                await LoadAndPlayPlaylistAsync(global, ct);
            }
        }
        // Already on global, or global is empty/unconfigured — do nothing.
    }

    public event Action<string>? PlaylistChanged;
    public event Action<string>? TrackChanged;

    /// <summary>
    /// Manually cycles to the next/previous playlist among all configured playlists
    /// that have at least one track — independent of the current category, so
    /// pressing this always does something predictable rather than silently no-op'ing
    /// if the current category has no override. Unlike SwitchPlaylistAsync (automatic,
    /// silent), this raises PlaylistChanged so the UI can show a brief indicator.
    /// </summary>
    public async Task CyclePlaylistAsync(int direction, string? currentCategoryId, CancellationToken ct = default)
    {
        if (!IsMusicEnabled || _vlc is null) return;

        var all = await _playlistRepo.GetAllAsync(ct);

        // Cyclable pool: Global playlist(s) plus whichever are assigned to the
        // category currently being browsed — a category's playlist is "locked" to
        // that category, so it never appears while browsing somewhere else.
        var cyclable = all.Where(p =>
                p.TrackIds.Count > 0 &&
                (p.IsGlobal || (currentCategoryId is not null &&
                    p.CategoryIds.Any(id => string.Equals(id, currentCategoryId, StringComparison.OrdinalIgnoreCase)))))
            .ToList();

        if (cyclable.Count == 0) return;

        int currentIndex = _currentPlaylist is null
            ? -1
            : cyclable.FindIndex(p => p.Id == _currentPlaylist.Id);

        int nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + direction + cyclable.Count) % cyclable.Count;

        var next = cyclable[nextIndex];
        _logger.LogInformation("Manually cycled playlist -> {Name}", next.Name);
        await LoadAndPlayPlaylistAsync(next, ct);
        PlaylistChanged?.Invoke(next.Name);
    }

    public void Pause()
    {
        if (_musicPlayer?.IsPlaying == true)
            _musicPlayer.Pause();
    }

    public void Resume()
    {
        if (_musicPlayer is not null && !_musicPlayer.IsPlaying)
            _musicPlayer.Play();
    }

    public void Stop()
    {
        _musicPlayer?.Stop();
    }

    // ── Navigation sounds ──────────────────────────────────────────────────

    public void PlayNavigate() => PlaySoundFromPath(_config.Settings.SoundNavigatePath);
    public void PlayConfirm()  => PlaySoundFromPath(_config.Settings.SoundConfirmPath);
    public void PlayBack()     => PlaySoundFromPath(_config.Settings.SoundBackPath);
    public void PlayError()    => PlaySoundFromPath(_config.Settings.SoundErrorPath);

    /// <summary>
    /// Resolves the given path (absolute or exe-relative) and plays it.
    /// Falls back to the legacy {soundsRoot}/{name}.* search if the
    /// configured path doesn't exist, so existing installs keep working.
    /// </summary>
    private void PlaySoundFromPath(string configuredPath)
    {
        if (!IsSoundEnabled || _vlc is null) return;

        var resolved = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(configuredPath);

        if (!File.Exists(resolved)) return; // Missing — silent fallback

        PlaySoundFile(resolved);
    }

    private void PlaySoundFile(string path)
    {
        if (_vlc is null) return;
        var vlc = _vlc; // capture for closure — avoids nullable warning

        Task.Run(() =>
        {
            try
            {
                using var player = new MediaPlayer(vlc);
                using var media  = new LibVlcMedia(vlc, path);
                player.Volume = (int)(SoundVolume * 100);
                player.Media  = media;
                player.Play();

                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (player.IsPlaying && DateTime.UtcNow < deadline)
                    Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sound playback failed: {Path}", path);
            }
        });
    }

    // ── Internal playlist management ───────────────────────────────────────

    private async Task LoadAndPlayPlaylistAsync(AudioPlaylist playlist, CancellationToken ct = default)
    {
        // Resolve TrackIds against the shared track library before taking the lock —
        // this is the actual fix for tracks silently not playing: the old model
        // stored raw paths directly on the playlist, but the new one stores
        // references into the library, so this lookup has to happen somewhere.
        // A track Id that no longer exists in the library (e.g. deleted after being
        // added to this playlist) is silently skipped, same as a missing file
        // always was.
        var allTracks = await _trackRepo.GetAllAsync(ct);
        var trackMap = allTracks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var resolvedPaths = new List<string>();
        foreach (var trackId in playlist.TrackIds)
        {
            if (trackMap.TryGetValue(trackId, out var track) && !string.IsNullOrWhiteSpace(track.Path))
                resolvedPaths.Add(track.Path);
            else
                _logger.LogWarning("Track {TrackId} referenced by playlist '{Playlist}' not found in the track library — skipping.", trackId, playlist.Name);
        }

        await _musicLock.WaitAsync(ct);
        try
        {
            if (_musicPlayer is null || _vlc is null) return;

            _musicPlayer.Stop();
            _currentPlaylist = playlist;
            ApplyEffectiveVolume();

            _shuffledTracks = playlist.Shuffle
                ? resolvedPaths.OrderBy(_ => Random.Shared.Next()).ToList()
                : resolvedPaths;

            _trackIndex = 0;
            PlayCurrentTrack(announceTrackChange: false);
        }
        finally { _musicLock.Release(); }
    }

    private void PlayCurrentTrack(bool announceTrackChange = true)
    {
        if (_musicPlayer is null || _vlc is null) return;
        if (_shuffledTracks.Count == 0) return;

        // Resolve to absolute first — track paths can now be stored relative to the
        // app's own folder (for drive-letter portability), and this was previously
        // never resolved at all, relying entirely on tracks always being stored
        // absolute. That was harmless only because nothing ever stored a relative
        // one; it would have silently depended on the process's current working
        // directory the moment one did.
        var path = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(_shuffledTracks[_trackIndex]);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Music track not found, skipping: {Path}", path);
            AdvanceTrack();
            return;
        }

        try
        {
            var media = new LibVlcMedia(_vlc, path);
            _musicPlayer.Media = media;
            int myGeneration = ++_playGeneration;
            _musicPlayer.Play();
            _logger.LogDebug("Now playing: {Track}", Path.GetFileName(path));

            if (announceTrackChange)
                TrackChanged?.Invoke(Path.GetFileNameWithoutExtension(path));

            // Verify playback actually started and retry (a few times, with
            // increasing delays) if not. LibVLC's very first Play() call after the
            // instance is created can occasionally race with internal setup and
            // silently no-op — most noticeable right at app startup, where a later
            // Pause()/Resume() cycle "fixes" it by calling Play() again once
            // everything's warmed up. Uses a generation counter rather than
            // reference-comparing the Media object, since LibVLCSharp may hand back
            // a different managed wrapper on each .Media read even for the same
            // underlying native resource — comparing objects directly here was
            // silently never matching, and so never actually retrying.
            _ = VerifyPlaybackStartedAsync(myGeneration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to play track: {Path}", path);
            AdvanceTrack();
        }
    }

    private async Task VerifyPlaybackStartedAsync(int myGeneration)
    {
        int[] delaysMs = [300, 700, 1500, 3000];
        foreach (var delay in delaysMs)
        {
            await Task.Delay(delay);

            // Only retry if we're still trying to play the SAME thing this check was
            // for — if the user has since skipped or switched tracks, that's already
            // a separate, newer Play() call in flight, and this stale check should
            // do nothing rather than stepping on it.
            if (_musicPlayer is null || myGeneration != _playGeneration)
            {
                return;
            }

            if (_musicPlayer.IsPlaying)
            {
                return;
            }

            try { _musicPlayer.Play(); }
            catch { /* best-effort retry - if this also fails, there's nothing further to do */ }
        }
    }

    private void OnTrackEnded(object? sender, EventArgs e)
    {
        // LibVLC raises this on a background thread — safe to call AdvanceTrack directly
        AdvanceTrack();
    }

    private void AdvanceTrack()
    {
        if (_shuffledTracks.Count == 0) return;
        _trackIndex = (_trackIndex + 1) % _shuffledTracks.Count;
        // Small delay to let LibVLC finish cleanup before starting next track
        Task.Delay(100).ContinueWith(_ => PlayCurrentTrack());
    }

    public void SkipToNextTrack()
    {
        if (_shuffledTracks.Count == 0) return;
        _trackIndex = (_trackIndex + 1) % _shuffledTracks.Count;
        // Same short delay as the automatic AdvanceTrack path, for the same reason —
        // letting LibVLC finish cleaning up the outgoing track before starting the
        // next one, regardless of whether the skip was user- or auto-triggered.
        Task.Delay(100).ContinueWith(_ => PlayCurrentTrack());
    }

    public void SkipToPreviousTrack()
    {
        if (_shuffledTracks.Count == 0) return;
        _trackIndex = (_trackIndex - 1 + _shuffledTracks.Count) % _shuffledTracks.Count;
        Task.Delay(100).ContinueWith(_ => PlayCurrentTrack());
    }

    // ── Dispose ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _musicPlayer?.Stop();
        _musicPlayer?.Dispose();
        _vlc?.Dispose();
        _musicLock.Dispose();
        _logger.LogInformation("LibVlcAudioService disposed.");
    }
}
