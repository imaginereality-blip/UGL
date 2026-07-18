using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;
using LibVlcCore = LibVLCSharp.Shared.Core;
using LibVlcMedia = LibVLCSharp.Shared.Media;

namespace UGL.Media;

/// <summary>
/// Production IVideoPreviewService. Maintains a single LibVLC + MediaPlayer instance
/// for the lifetime of the app. Only one card plays video at a time.
///
/// The View wires the MediaPlayer to its VideoView via GetMediaPlayer().
/// Audio is optional — controlled by AppSettings.VideoPreviewAudio.
/// When audio is disabled the MediaPlayer volume is set to 0.
/// </summary>
public sealed class VideoPreviewService : IVideoPreviewService
{
    private readonly ILogger<VideoPreviewService> _logger;
    private LibVLC? _vlc;
    private MediaPlayer? _player;

    public bool  IsEnabled    { get; }
    public bool  AudioEnabled { get; }

    public float Volume
    {
        get => _player is not null ? _player.Volume / 100f : 0f;
        set { if (_player is not null) _player.Volume = (int)(value * 100); }
    }

    public VideoPreviewService(AppSettings settings, ILogger<VideoPreviewService> logger)
    {
        _logger = logger;
        IsEnabled    = settings.EnableVideoPreview;
        AudioEnabled = settings.VideoPreviewAudio;

        if (!IsEnabled) return;

        try
        {
            LibVlcCore.Initialize();
            _vlc    = new LibVLC(enableDebugLogs: false);
            _player = new MediaPlayer(_vlc)
            {
                Volume = AudioEnabled ? (int)(settings.VideoPreviewVolume * 100) : 0
            };
            _logger.LogInformation("VideoPreviewService initialized (audio: {Audio}).", AudioEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VideoPreviewService failed to initialize. Video preview disabled.");
            _vlc    = null;
            _player = null;
        }
    }

    public void Play(string absolutePath)
    {
        if (_player is null || _vlc is null) return;
        if (!File.Exists(absolutePath))
        {
            _logger.LogDebug("Video file not found, skipping preview: {Path}", absolutePath);
            return;
        }

        try
        {
            _player.Stop();
            using var media = new LibVlcMedia(_vlc, absolutePath);
            // Loop the video indefinitely
            media.AddOption(":input-repeat=65535");
            _player.Media = media;
            _player.Play();
            _logger.LogDebug("Video preview started: {File}", Path.GetFileName(absolutePath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start video preview: {Path}", absolutePath);
        }
    }

    public void Stop()
    {
        try { _player?.Stop(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error stopping video preview."); }
    }

    public object? GetMediaPlayer() => _player;

    public void Dispose()
    {
        Stop();
        _player?.Dispose();
        _vlc?.Dispose();
        _logger.LogInformation("VideoPreviewService disposed.");
    }
}
