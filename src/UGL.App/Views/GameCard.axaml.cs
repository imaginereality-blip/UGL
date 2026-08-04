using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using UGL.App.ViewModels;

namespace UGL.App.Views;

public sealed partial class GameCard : UserControl
{
    // Cached once in the constructor, right after InitializeComponent — the visual
    // tree is fully built by then and these controls never change identity for the
    // lifetime of this instance, so there's no reason to repeat FindControl on every
    // selection change / video start-stop, which previously ran on every property
    // change notification.
    private readonly Border?    _cardBorder;
    private readonly Image?     _coverImage;
    private readonly TextBlock? _placeholderText;
    private readonly Border?    _videoContainer;

    private VideoView? _videoView;
    private GameCardViewModel? _vm;
    private DispatcherTimer? _pulseTimer;
    private DateTime _pulseStart;

    public GameCard()
    {
        InitializeComponent();

        _cardBorder      = this.FindControl<Border>("CardBorder");
        _coverImage      = this.FindControl<Image>("CoverImage");
        _placeholderText = this.FindControl<TextBlock>("PlaceholderText");
        _videoContainer  = this.FindControl<Border>("VideoContainer");

        DataContextChanged += OnDataContextChanged;

        // Subscribed once here, not in OnDataContextChanged — that fires every time the
        // VM pool reassigns this card, and re-subscribing each time would stack up
        // duplicate handlers. The card pool is small and bounded (five instances), so a
        // single subscription for the lifetime of the control is safe.
        CardHighlightSettings.Changed += OnHighlightSettingsChanged;

        if (_cardBorder is not null)
            _cardBorder.SizeChanged += OnCardSizeChanged;
    }

    private void OnCardSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        CardDimensionInfo.ReportGameCardSize(new Size(e.NewSize.Width * scaling, e.NewSize.Height * scaling));
    }

    private void OnHighlightSettingsChanged()
    {
        if (_vm is not null) ApplySelectionStyle(_vm.IsSelected);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) { StopVideo(); _vm = null; }
        if (DataContext is not GameCardViewModel vm) return;
        _vm = vm;

        vm.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(GameCardViewModel.IsSelected):
                    ApplySelectionStyle(vm.IsSelected);
                    if (vm.IsSelected) TryStartVideo(vm);
                    else StopVideo();
                    break;
                case nameof(GameCardViewModel.CoverBitmap):
                    if (!vm.IsVideoActive) ApplyCoverVisibility(vm.CoverBitmap is not null);
                    break;
                case nameof(GameCardViewModel.VideoPath):
                    if (vm.IsSelected && vm.VideoPath is not null) TryStartVideo(vm);
                    break;
            }
        };

        ApplySelectionStyle(vm.IsSelected);
        ApplyCoverVisibility(vm.CoverBitmap is not null);
    }

    private void ApplySelectionStyle(bool isSelected)
    {
        if (_cardBorder is not { } b) return;

        StopPulse();

        if (!isSelected)
        {
            b.BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF"));
            b.BorderThickness = new Thickness(0);
            b.Effect = null;
            return;
        }

        var baseColor = ParseColorSafe(CardHighlightSettings.Color);
        b.BorderThickness = new Thickness(CardHighlightSettings.Thickness);

        if (string.Equals(CardHighlightSettings.Style, "Pulsing", StringComparison.OrdinalIgnoreCase))
            StartPulse(b, baseColor);
        else
        {
            b.BorderBrush = new SolidColorBrush(baseColor) { Opacity = CardHighlightSettings.Intensity };
            b.Effect = MakeGlow(baseColor, CardHighlightSettings.Intensity);
        }
    }

    /// <summary>
    /// Soft glow kept alongside (not replacing) the existing selection border — reads
    /// as the card being lit from behind rather than a rectangle drawn on top of it.
    /// </summary>
    private static DropShadowEffect MakeGlow(Color baseColor, double intensity) => new()
    {
        Color = baseColor,
        BlurRadius = 24,
        OffsetX = 0,
        OffsetY = 0,
        Opacity = Math.Clamp(intensity * 0.65, 0, 1),
    };

    private void StartPulse(Border b, Color baseColor)
    {
        _pulseStart = DateTime.UtcNow;
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _pulseTimer.Tick += (_, _) =>
        {
            var elapsedSeconds = (DateTime.UtcNow - _pulseStart).TotalSeconds;
            // Smooth sine pulse, ~1.5s period, floor at 30% so it never fully vanishes.
            double wave = (Math.Sin(elapsedSeconds * Math.PI * 1.3) + 1) / 2; // 0..1
            double opacity = Math.Clamp((0.3 + wave * 0.7) * CardHighlightSettings.Intensity, 0, 1);
            b.BorderBrush = new SolidColorBrush(baseColor) { Opacity = opacity };
            b.Effect = MakeGlow(baseColor, opacity);
        };
        _pulseTimer.Start();
    }

    private void StopPulse()
    {
        if (_pulseTimer is null) return;
        _pulseTimer.Stop();
        _pulseTimer = null;
    }

    private static Color ParseColorSafe(string hex)
    {
        try { return Color.Parse(hex); }
        catch { return Color.Parse("#FFFFD700"); }
    }

    private void ApplyCoverVisibility(bool hasCover)
    {
        if (_coverImage is not null)
        {
            _coverImage.IsVisible = hasCover;
            // Always Fill — stretches image to exact card dimensions, no cropping.
            _coverImage.Stretch = Stretch.Fill;
        }

        if (_placeholderText is not null)
            _placeholderText.IsVisible = !hasCover;
    }

    private void TryStartVideo(GameCardViewModel vm)
    {
        if (vm.VideoPath is null) return;

        var browser = GetBrowserViewModel();
        if (browser is null) return;

        var service = browser.VideoPreview;
        if (!service.IsEnabled) return;

        var player = service.GetMediaPlayer() as MediaPlayer;
        if (player is null) return;

        if (_videoView is null)
            _videoView = new VideoView
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Stretch,
                MediaPlayer         = player
            };

        if (_videoContainer is not null)
        {
            _videoContainer.Child     = _videoView;
            _videoContainer.IsVisible = true;
        }

        if (_coverImage is not null)      _coverImage.IsVisible = false;
        if (_placeholderText is not null) _placeholderText.IsVisible = false;

        vm.IsVideoActive = true;
        service.Play(vm.VideoPath);
    }

    private void StopVideo()
    {
        if (_vm is not null) _vm.IsVideoActive = false;

        GetBrowserViewModel()?.VideoPreview.Stop();

        if (_videoContainer is not null)
        {
            _videoContainer.IsVisible = false;
            _videoContainer.Child     = null;
        }

        if (_vm is not null) ApplyCoverVisibility(_vm.CoverBitmap is not null);
    }

    private GameBrowserViewModel? GetBrowserViewModel()
    {
        Control? current = this;
        while (current is not null)
        {
            if (current is UserControl uc && uc.DataContext is GameBrowserViewModel browser)
                return browser;
            current = current.Parent as Control;
        }
        return null;
    }
}
