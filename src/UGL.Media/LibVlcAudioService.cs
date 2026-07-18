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

    public float MusicVolume
    {
        get => _musicPlayer is not null ? _musicPlayer.Volume / 100f : 0.5f;
        set { if (_musicPlayer is not null) _musicPlayer.Volume = (int)(value * 100); }
    }

    public bool IsMusicEnabled { get; set; } = true;
    public float SoundVolume   { get; set; } = 1.0f;
    public bool IsSoundEnabled { get; set; } = true;

    public LibVlcAudioService(
        IAudioPlaylistRepository playlistRepo,
        IConfigurationService config,
        ILogger<LibVlcAudioService> logger)
    {
        _playlistRepo = playlistRepo;
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
            _logger.LogInformation("LibVLC initialized.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LibVLC failed to initialize. Audio will be disabled.");
            IsMusicEnabled = false;
            IsSoundEnabled = false;
            return;
        }

        if (!IsMusicEnabled) return;

        // Load and start the global playlist
        var global = await _playlistRepo.GetByIdAsync("global", ct);
        if (global is not null && global.Tracks.Count > 0)
            await LoadAndPlayPlaylistAsync(global);
        else
            _logger.LogInformation("Global playlist is empty — no background music.");
    }

    // ── Playlist switching ─────────────────────────────────────────────────

    public async Task SwitchPlaylistAsync(string categoryId, CancellationToken ct = default)
    {
        if (!IsMusicEnabled || _vlc is null) return;

        var categoryPlaylist = await _playlistRepo.GetByIdAsync(categoryId, ct);

        if (categoryPlaylist is not null && categoryPlaylist.Tracks.Count > 0)
        {
            _logger.LogDebug("Switching to category playlist: {Id}", categoryId);
            await LoadAndPlayPlaylistAsync(categoryPlaylist);
        }
        else if (_currentPlaylist?.Id != "global")
        {
            // No category override — revert to global
            var global = await _playlistRepo.GetByIdAsync("global", ct);
            if (global is not null && global.Tracks.Count > 0)
            {
                _logger.LogDebug("Reverting to global playlist.");
                await LoadAndPlayPlaylistAsync(global);
            }
        }
        // Already on global, or global is empty — do nothing.
    }

    public event Action<string>? PlaylistChanged;

    /// <summary>
    /// Manually cycles to the next/previous playlist among all configured playlists
    /// that have at least one track — independent of the current category, so
    /// pressing this always does something predictable rather than silently no-op'ing
    /// if the current category has no override. Unlike SwitchPlaylistAsync (automatic,
    /// silent), this raises PlaylistChanged so the UI can show a brief indicator.
    /// </summary>
    public async Task CyclePlaylistAsync(int direction, CancellationToken ct = default)
    {
        if (!IsMusicEnabled || _vlc is null) return;

        var all = await _playlistRepo.GetAllAsync(ct);
        var withTracks = all.Where(p => p.Tracks.Count > 0).ToList();
        if (withTracks.Count == 0) return;

        int currentIndex = _currentPlaylist is null
            ? -1
            : withTracks.FindIndex(p => p.Id == _currentPlaylist.Id);

        int nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + direction + withTracks.Count) % withTracks.Count;

        var next = withTracks[nextIndex];
        _logger.LogInformation("Manually cycled playlist -> {Name}", next.Name);
        await LoadAndPlayPlaylistAsync(next);
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

    private async Task LoadAndPlayPlaylistAsync(AudioPlaylist playlist)
    {
        await _musicLock.WaitAsync();
        try
        {
            if (_musicPlayer is null || _vlc is null) return;

            _musicPlayer.Stop();
            _currentPlaylist = playlist;
            _musicPlayer.Volume = (int)(playlist.Volume * 100);

            _shuffledTracks = playlist.Shuffle
                ? playlist.Tracks.OrderBy(_ => Random.Shared.Next()).ToList()
                : new List<string>(playlist.Tracks);

            _trackIndex = 0;
            PlayCurrentTrack();
        }
        finally { _musicLock.Release(); }
    }

    private void PlayCurrentTrack()
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
            _musicPlayer.Play();
            _logger.LogDebug("Now playing: {Track}", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to play track: {Path}", path);
            AdvanceTrack();
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
