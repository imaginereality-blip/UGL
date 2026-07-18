using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

public sealed partial class CardHighlightConfigViewModel : ObservableObject
{
    private readonly IConfigurationService _config;
    private readonly VirtualKeyboardViewModel _virtualKeyboard;
    private readonly ILogger<CardHighlightConfigViewModel> _logger;

    // Full HSV color space — any hue, any saturation, any brightness. This is the
    // "any color on the wheel" the sliders give you, just without a literal circular
    // wheel widget (that needs either an external package or a fair amount of custom
    // pointer-tracking canvas code — sliders get you the same reachable color space
    // with far less risk).
    [ObservableProperty] private double _hue = 50;        // 0-360
    [ObservableProperty] private double _saturation = 1.0; // 0-1
    [ObservableProperty] private double _value = 1.0;      // 0-1 (brightness)

    [ObservableProperty] private double _intensity = 1.0; // border opacity, 0.1-1.0
    [ObservableProperty] private int _thickness = 4;       // pixels, 2-5
    [ObservableProperty] private bool _isPulsing;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public string StyleLabel => IsPulsing ? "Pulsing" : "Solid";

    private bool _syncingFromHex;

    public string CustomHex => ColorToHex(HsvToRgb(Hue, Saturation, Value));

    /// <summary>
    /// A real IBrush, not a string. The preview border binds to this instead of
    /// CustomHex directly — binding a plain string to a Brush-typed property only
    /// reliably converts on the *initial* bind in Avalonia; it doesn't reconvert on
    /// every subsequent value change the way a genuinely-typed IBrush property does,
    /// which is why the live preview wasn't updating.
    /// </summary>
    public IBrush PreviewBrush => new SolidColorBrush(HsvToRgb(Hue, Saturation, Value));

    /// <summary>
    /// A real Avalonia.Thickness, not a raw int — same reasoning as PreviewBrush above.
    /// Binding a plain number to a Thickness-typed property has the same "only reliably
    /// converts on the initial bind" risk as binding a string to a Brush.
    /// </summary>
    public Avalonia.Thickness PreviewThickness => new(Thickness);

    public string HexDisplay => CustomHex;
    public string HslDisplay
    {
        get
        {
            var (h, s, l) = HsvToHsl(Hue, Saturation, Value);
            return $"hsl({Math.Round(h)}deg {Math.Round(s * 100)}% {Math.Round(l * 100)}%)";
        }
    }
    public string RgbDisplay
    {
        get
        {
            var c = HsvToRgb(Hue, Saturation, Value);
            return $"rgb({c.R} {c.G} {c.B})";
        }
    }

    public CardHighlightConfigViewModel(
        IConfigurationService config,
        VirtualKeyboardViewModel virtualKeyboard,
        ILogger<CardHighlightConfigViewModel> logger)
    {
        _config = config;
        _virtualKeyboard = virtualKeyboard;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var s = _config.Settings;
        Intensity = s.CardHighlightIntensity;
        Thickness = Math.Clamp(s.CardHighlightThickness, 2, 5);
        IsPulsing = string.Equals(s.CardHighlightStyle, "Pulsing", StringComparison.OrdinalIgnoreCase);

        SetFromHex(s.CardHighlightColor);

        CardHighlightSettings.Load(CustomHex, Intensity, IsPulsing ? "Pulsing" : "Solid", Thickness);
        await Task.CompletedTask;
    }

    private void SetFromHex(string hex)
    {
        _syncingFromHex = true;
        try
        {
            var color = ParseColorSafe(hex);
            var (h, s, v) = RgbToHsv(color);
            Hue = h;
            Saturation = s;
            Value = v;
        }
        finally { _syncingFromHex = false; }
    }

    partial void OnHueChanged(double value) => OnHsvChanged();
    partial void OnSaturationChanged(double value) => OnHsvChanged();
    partial void OnValueChanged(double value) => OnHsvChanged();

    private void OnHsvChanged()
    {
        if (_syncingFromHex) return;
        OnPropertyChanged(nameof(CustomHex));
        OnPropertyChanged(nameof(PreviewBrush));
        OnPropertyChanged(nameof(HexDisplay));
        OnPropertyChanged(nameof(HslDisplay));
        OnPropertyChanged(nameof(RgbDisplay));
        PushLive();
    }

    partial void OnIntensityChanged(double value) => PushLive();
    partial void OnThicknessChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewThickness));
        PushLive();
    }
    partial void OnIsPulsingChanged(bool value)
    {
        OnPropertyChanged(nameof(StyleLabel));
        PushLive();
    }

    /// <summary>Updates the static live-settings bridge immediately, so any visible,
    /// selected card reflects the change right away — not just after Save.</summary>
    private void PushLive()
        => CardHighlightSettings.Load(CustomHex, Intensity, IsPulsing ? "Pulsing" : "Solid", Thickness);

    // ── Field highlight ───────────────────────────────────────────────────
    [ObservableProperty] private int _focusIndex;
    private const int PositionCount = 7; // Hue, Saturation, Value, Thickness, Intensity, Style, Save

    public bool IsHueFocused        => FocusIndex == 0;
    public bool IsSaturationFocused => FocusIndex == 1;
    public bool IsValueFocused      => FocusIndex == 2;
    public bool IsThicknessFocused  => FocusIndex == 3;
    public bool IsIntensityFocused  => FocusIndex == 4;
    public bool IsStyleFocused      => FocusIndex == 5;
    public bool IsSaveFocused       => FocusIndex == 6;

    partial void OnFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsHueFocused));
        OnPropertyChanged(nameof(IsSaturationFocused));
        OnPropertyChanged(nameof(IsValueFocused));
        OnPropertyChanged(nameof(IsThicknessFocused));
        OnPropertyChanged(nameof(IsIntensityFocused));
        OnPropertyChanged(nameof(IsStyleFocused));
        OnPropertyChanged(nameof(IsSaveFocused));
    }

    // ── Controller navigation ────────────────────────────────────────────
    public void NavigateUp() => FocusIndex = (FocusIndex - 1 + PositionCount) % PositionCount;
    public void NavigateDown() => FocusIndex = (FocusIndex + 1) % PositionCount;

    public void NavigateLeft()
    {
        switch (FocusIndex)
        {
            case 0: Hue = Math.Max(0, Hue - 10); break;
            case 1: Saturation = Math.Max(0, Math.Round(Saturation - 0.05, 2)); break;
            case 2: Value = Math.Max(0, Math.Round(Value - 0.05, 2)); break;
            case 3: Thickness = Math.Max(2, Thickness - 1); break;
            case 4: Intensity = Math.Max(0.1, Math.Round(Intensity - 0.1, 1)); break;
            case 5: IsPulsing = !IsPulsing; break;
        }
    }

    public void NavigateRight()
    {
        switch (FocusIndex)
        {
            case 0: Hue = Math.Min(360, Hue + 10); break;
            case 1: Saturation = Math.Min(1, Math.Round(Saturation + 0.05, 2)); break;
            case 2: Value = Math.Min(1, Math.Round(Value + 0.05, 2)); break;
            case 3: Thickness = Math.Min(5, Thickness + 1); break;
            case 4: Intensity = Math.Min(1.0, Math.Round(Intensity + 0.1, 1)); break;
            case 5: IsPulsing = !IsPulsing; break;
        }
    }

    public async Task ConfirmAsync()
    {
        switch (FocusIndex)
        {
            case 5:
                IsPulsing = !IsPulsing;
                break;
            case 6:
                await SaveAsync();
                break;
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = _config.Settings;
        var updated = new AppSettings
        {
            MediaRootPath          = s.MediaRootPath,
            RomsRootPath           = s.RomsRootPath,
            EmulatorsRootPath      = s.EmulatorsRootPath,
            AddonsRootPath         = s.AddonsRootPath,
            LogsRootPath           = s.LogsRootPath,
            ActiveThemeId          = s.ActiveThemeId,
            DefaultCategoryId      = s.DefaultCategoryId,
            Language               = s.Language,
            TargetFrameRate        = s.TargetFrameRate,
            EnableBackgroundMusic  = s.EnableBackgroundMusic,
            EnableNavigationSounds = s.EnableNavigationSounds,
            MusicVolume            = s.MusicVolume,
            SoundVolume            = s.SoundVolume,
            SoundNavigatePath      = s.SoundNavigatePath,
            SoundConfirmPath       = s.SoundConfirmPath,
            SoundBackPath          = s.SoundBackPath,
            SoundErrorPath         = s.SoundErrorPath,
            EnableVideoPreview     = s.EnableVideoPreview,
            VideoPreviewDelayMs    = s.VideoPreviewDelayMs,
            VideoPreviewAudio      = s.VideoPreviewAudio,
            VideoPreviewVolume     = s.VideoPreviewVolume,
            CardHighlightColor     = CustomHex,
            CardHighlightIntensity = Intensity,
            CardHighlightStyle     = IsPulsing ? "Pulsing" : "Solid",
            CardHighlightThickness = Thickness,
        };

        await _config.UpdateSettingsAsync(updated);
        CardHighlightSettings.Load(CustomHex, Intensity, IsPulsing ? "Pulsing" : "Solid", Thickness);
        StatusMessage = "Card highlight saved.";
        _logger.LogInformation(
            "Card highlight saved: {Color} intensity={Intensity} style={Style} thickness={Thickness}",
            CustomHex, Intensity, IsPulsing ? "Pulsing" : "Solid", Thickness);
    }

    // ── HSV <-> RGB (plain math, deliberately not relying on any Avalonia-specific
    // HSV API — keeps this correct regardless of exact framework version) ──────────

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r, g, b;
        if (h < 60)       { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else              { r = c; g = 0; b = x; }

        byte R = (byte)Math.Round((r + m) * 255);
        byte G = (byte)Math.Round((g + m) * 255);
        byte B = (byte)Math.Round((b + m) * 255);
        return Color.FromRgb(R, G, B);
    }

    private static (double h, double s, double v) RgbToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0.0001)
        {
            if (Math.Abs(max - r) < 0.0001)      h = 60 * (((g - b) / delta) % 6);
            else if (Math.Abs(max - g) < 0.0001) h = 60 * (((b - r) / delta) + 2);
            else                                 h = 60 * (((r - g) / delta) + 4);
        }
        if (h < 0) h += 360;

        double s = max <= 0.0001 ? 0 : delta / max;
        double v = max;

        return (h, s, v);
    }

    private static (double h, double s, double l) HsvToHsl(double h, double sv, double v)
    {
        double l = v * (1 - sv / 2);
        double sl = (l <= 0 || l >= 1) ? 0 : (v - l) / Math.Min(l, 1 - l);
        return (h, sl, l);
    }

    private static string ColorToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color ParseColorSafe(string hex)
    {
        try { return Color.Parse(hex); }
        catch { return Color.Parse("#FFFFD700"); }
    }
}
