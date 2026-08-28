using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using UGL.App.ViewModels;
using LibVlcCore = LibVLCSharp.Shared.Core;
using LibVlcMedia = LibVLCSharp.Shared.Media;

namespace UGL.App.Views;

/// <summary>
/// Owns its own standalone LibVLC + MediaPlayer instance, entirely separate from
/// VideoPreviewService (the game-card hover-preview player) — that service is gated
/// on the user's "enable video preview" setting and loops indefinitely, neither of
/// which is right for a startup cinematic that must always play once regardless of
/// that setting. Plays StartupAssets/ReplayLauncherStartupVideo.mp4 once on load and
/// calls MainWindowViewModel.SkipStartupVideo() when it ends; MainWindowViewModel
/// also calls that on any controller/keyboard input, at which point this control's
/// host (StartupSplashWindow) closes and tears the player down via Unloaded.
///
/// Lives as the sole content of a dedicated top-level StartupSplashWindow rather than
/// as a layer inside MainWindow's own Panel — an earlier version tried the latter and
/// the video never visibly played, most likely lost to some interaction with
/// MainWindow's own IsVisible/Panel z-order/binding timing during its own
/// construction. A separate window sidesteps all of that: it opens immediately,
/// independent of anything MainWindow is doing, and MainWindow loads normally,
/// unblocked, behind it (see App.axaml.cs).
/// </summary>
public sealed partial class StartupVideoView : UserControl
{
    private static readonly string VideoPath =
        Path.Combine(AppContext.BaseDirectory, "StartupAssets", "ReplayLauncherStartupVideo.mp4");

    private readonly Panel? _videoHost;
    private readonly ILogger<StartupVideoView>? _logger;
    private LibVLC? _vlc;
    private MediaPlayer? _player;
    private VideoView? _videoView;
    private MainWindowViewModel? _vm;

    public StartupVideoView()
    {
        InitializeComponent();
        _videoHost = this.FindControl<Panel>("VideoHost");
        _logger = App.Services.GetService(typeof(ILogger<StartupVideoView>)) as ILogger<StartupVideoView>;

        _logger?.LogInformation("StartupVideoView constructed. VideoHost found: {Found}. VideoPath: {Path}",
            _videoHost is not null, VideoPath);

        DataContextChanged += (_, _) => _vm = DataContext as MainWindowViewModel;
        Loaded += (_, _) => { _logger?.LogInformation("StartupVideoView.Loaded fired."); Start(); };
        // Safety net only — the real teardown path is StartupSplashWindow calling
        // StopPlayback() explicitly BEFORE it closes (see that class and StopPlayback's
        // own doc comment for why order matters here). StopPlayback is idempotent, so
        // this is a no-op on the expected path.
        Unloaded += (_, _) => { _logger?.LogInformation("StartupVideoView.Unloaded fired."); StopPlayback(); };
    }

    private void Start()
    {
        if (_player is not null)
        {
            _logger?.LogInformation("Start() called but a player already exists — ignoring.");
            return;
        }

        if (!File.Exists(VideoPath))
        {
            _logger?.LogWarning("Startup video file not found at {Path} — skipping.", VideoPath);
            _vm?.SkipStartupVideo();
            return;
        }

        try
        {
            _logger?.LogInformation("Initializing LibVLC for startup video...");
            LibVlcCore.Initialize();
            _vlc = new LibVLC(enableDebugLogs: false);
            _player = new MediaPlayer(_vlc) { Volume = 90 };
            _player.EndReached += (_, _) =>
            {
                _logger?.LogInformation("Startup video EndReached.");
                Dispatcher.UIThread.Post(() => _vm?.SkipStartupVideo());
            };
            _player.EncounteredError += (_, _) => _logger?.LogWarning("LibVLC MediaPlayer reported EncounteredError during startup video playback.");
            _player.Playing += (_, _) => _logger?.LogInformation("LibVLC MediaPlayer reports Playing.");

            _videoView = new VideoView
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                MediaPlayer = _player,
            };
            _videoHost?.Children.Insert(0, _videoView);
            _logger?.LogInformation("VideoView created and inserted into host (host null: {HostNull}).", _videoHost is null);

            using var media = new LibVlcMedia(_vlc, VideoPath);
            _player.Media = media;
            bool started = _player.Play();
            _logger?.LogInformation("LibVLC Play() returned {Started}.", started);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Startup video failed to play — skipping.");
            _vm?.SkipStartupVideo();
        }
    }

    /// <summary>
    /// Idempotent — safe to call more than once (StartupSplashWindow calls this
    /// explicitly before closing; Unloaded calls it again as a safety net).
    ///
    /// Order matters: an earlier version relied solely on Unloaded, which fires
    /// WHILE the enclosing Window is already tearing itself (and its native Win32
    /// handle) down — disposing the LibVLC player and detaching its native rendering
    /// surface AT THE SAME TIME the host window is also being destroyed crashed the
    /// whole process natively (no managed exception, no AppDomain.UnhandledException,
    /// no crash.log — confirmed from a real run's log, which just stops mid-teardown).
    /// Clearing VideoView.MediaPlayer first, then removing the view from the tree,
    /// then disposing the player/vlc — all while the window is still fully alive and
    /// stable — avoids that race entirely.
    /// </summary>
    public void StopPlayback()
    {
        if (_player is null && _vlc is null) return;
        _logger?.LogInformation("StopPlayback() tearing down startup video player.");

        try { _player?.Stop(); }
        catch { /* best-effort */ }

        if (_videoView is not null) _videoView.MediaPlayer = null;

        if (_videoView is not null && _videoHost is not null)
            _videoHost.Children.Remove(_videoView);

        _videoView = null;
        _player?.Dispose();
        _player = null;
        _vlc?.Dispose();
        _vlc = null;
    }
}
