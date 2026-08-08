using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.App.Services;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

/// <summary>
/// Settings → Title Graphics tab. Configures the category-card title-text
/// overlay — on/off, scale/position, and the 3D bake style (fill gradient,
/// bevel/outline colors, up to 3 accent lights, X/Y/Z rotation) used by
/// TitleGraphicsBaker to render the real three.js scene per category. The text
/// itself is always the category's own Label (see CategoryCardViewModel), not
/// something this tab lets you override.
///
/// Colors are edited through a collapsible HSV color-wheel sub-panel (same
/// ColorWheelPicker control as Settings → Card Highlight) rather than 8 separate
/// wheels on screen at once — clicking a color swatch opens the panel for that one
/// field (BeginEditColor), and it collapses back to the normal field list when done
/// (CloseColorPicker). See IsEditingColor/EditingColorKey below.
/// </summary>
public sealed partial class TitleGraphicsConfigViewModel : ObservableObject
{
    private readonly IConfigurationService _config;
    private readonly TitleGraphicsBaker _baker;
    private readonly ILogger<TitleGraphicsConfigViewModel> _logger;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private double _scale = 0.9;        // 0.5-2.5
    [ObservableProperty] private double _positionX;         // -50..50, % of card width
    [ObservableProperty] private double _positionY;         // -50..50, % of card height

    [ObservableProperty] private string _fillTopColor = "#B8860B";
    [ObservableProperty] private string _fillMidColor = "#E8C848";
    [ObservableProperty] private string _fillBottomColor = "#FFF3C4";
    [ObservableProperty] private string _bevelColor = "#A855FF";
    [ObservableProperty] private string _outlineColor = "#C41E1E";

    [ObservableProperty] private int _lightCount = 2; // 1-3
    [ObservableProperty] private string _lightMainColor = "#F5C518";
    [ObservableProperty] private string _lightKey1Color = "#3D1466";
    [ObservableProperty] private string _lightKey2Color = "#FFFFFF";

    [ObservableProperty] private double _rotationXDegrees;
    [ObservableProperty] private double _rotationYDegrees;
    [ObservableProperty] private double _rotationZDegrees;

    public bool IsKey1LightUsed => LightCount >= 2;
    public bool IsKey2LightUsed => LightCount >= 3;

    public TitleGraphicsConfigViewModel(
        IConfigurationService config,
        TitleGraphicsBaker baker,
        ILogger<TitleGraphicsConfigViewModel> logger)
    {
        _config = config;
        _baker = baker;
        _logger = logger;

        // Same live-measurement bridge CategoriesConfigViewModel uses for its
        // "recommended art size" hint — the preview pane's shape should track the
        // real, currently-rendered category card aspect (which depends on window
        // size, not a fixed constant), not an arbitrary guessed box.
        CardDimensionInfo.Changed += OnCardDimensionsChanged;
    }

    private void OnCardDimensionsChanged()
    {
        OnPropertyChanged(nameof(PreviewPaneWidth));
        OnPropertyChanged(nameof(PreviewPaneHeight));
        OnPropertyChanged(nameof(PreviewTranslateX));
        OnPropertyChanged(nameof(PreviewTranslateY));
    }

    public Task InitializeAsync()
    {
        var s = _config.Settings;
        IsEnabled = s.TitleGraphicsEnabled;
        Scale = s.TitleGraphicsScale;
        PositionX = s.TitleGraphicsPositionX;
        PositionY = s.TitleGraphicsPositionY;
        FillTopColor = s.TitleGraphicsFillTopColor;
        FillMidColor = s.TitleGraphicsFillMidColor;
        FillBottomColor = s.TitleGraphicsFillBottomColor;
        BevelColor = s.TitleGraphicsBevelColor;
        OutlineColor = s.TitleGraphicsOutlineColor;
        LightCount = s.TitleGraphicsLightCount;
        LightMainColor = s.TitleGraphicsLightMainColor;
        LightKey1Color = s.TitleGraphicsLightKey1Color;
        LightKey2Color = s.TitleGraphicsLightKey2Color;
        RotationXDegrees = s.TitleGraphicsRotationXDegrees;
        RotationYDegrees = s.TitleGraphicsRotationYDegrees;
        RotationZDegrees = s.TitleGraphicsRotationZDegrees;
        TitleGraphicsSettings.Load(IsEnabled, Scale, PositionX, PositionY);
        PushLiveStyle();
        SchedulePreviewBake();
        return Task.CompletedTask;
    }

    partial void OnIsEnabledChanged(bool value) => PushLive();
    partial void OnScaleChanged(double value) => PushLive();
    partial void OnPositionXChanged(double value)
    {
        OnPropertyChanged(nameof(PreviewTranslateX));
        PushLive();
    }
    partial void OnPositionYChanged(double value)
    {
        OnPropertyChanged(nameof(PreviewTranslateY));
        PushLive();
    }

    partial void OnFillTopColorChanged(string value) => PushLiveStyle();
    partial void OnFillMidColorChanged(string value) => PushLiveStyle();
    partial void OnFillBottomColorChanged(string value) => PushLiveStyle();
    partial void OnBevelColorChanged(string value) => PushLiveStyle();
    partial void OnOutlineColorChanged(string value) => PushLiveStyle();
    partial void OnLightMainColorChanged(string value) => PushLiveStyle();
    partial void OnLightKey1ColorChanged(string value) => PushLiveStyle();
    partial void OnLightKey2ColorChanged(string value) => PushLiveStyle();
    partial void OnRotationXDegreesChanged(double value) => PushLiveStyle();
    partial void OnRotationYDegreesChanged(double value) => PushLiveStyle();
    partial void OnRotationZDegreesChanged(double value) => PushLiveStyle();

    partial void OnLightCountChanged(int value)
    {
        OnPropertyChanged(nameof(IsKey1LightUsed));
        OnPropertyChanged(nameof(IsKey2LightUsed));
        PushLiveStyle();
    }

    /// <summary>Updates the static live-settings bridge immediately, so any visible
    /// category card reflects the change right away — not just after Save.</summary>
    private void PushLive() => TitleGraphicsSettings.Load(IsEnabled, Scale, PositionX, PositionY);

    private void PushLiveStyle()
    {
        TitleGraphicsSettings.LoadStyle(
            FillTopColor, FillMidColor, FillBottomColor,
            BevelColor, OutlineColor,
            LightCount, LightMainColor, LightKey1Color, LightKey2Color,
            RotationXDegrees, RotationYDegrees, RotationZDegrees);

        // Any style field (color, light, rotation) changes what actually gets baked —
        // unlike Scale/PositionX/Y, which are a display-time transform applied to
        // whatever image is already showing and don't need a re-bake.
        SchedulePreviewBake();
    }

    // ── Preview bake — makes the preview pane show the REAL 3D-rendered look (gradient
    // fill under actual scene lighting, bevel, colored lights) rather than the flat,
    // hardcoded-color 2D fallback (CategoryTitleGraphic), which never reflected any of
    // these settings. Bakes to a synthetic category id, debounced so a slider drag
    // doesn't fire a bake per tick — only once ~400ms after the user stops moving it. ──
    private const string PreviewCategoryId = "__title_graphics_preview";
    private const string PreviewLabel = "CATEGORY TITLE";
    private static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(400);

    [ObservableProperty] private Bitmap? _previewBitmap;
    [ObservableProperty] private bool _isPreviewRendering;

    private CancellationTokenSource? _previewBakeCts;

    private void SchedulePreviewBake()
    {
        _previewBakeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewBakeCts = cts;
        IsPreviewRendering = true;
        _ = RunPreviewBakeAsync(cts.Token);
    }

    private async Task RunPreviewBakeAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(PreviewDebounce, ct);
            var path = await _baker.BakeCategoryAsync(PreviewCategoryId, PreviewLabel, ct);
            if (ct.IsCancellationRequested || path is null) return;

            // Loaded fresh (not cached) each time — the file changes on every bake and
            // this is a single preview image, not a pool of five cards, so the cost of
            // re-decoding is negligible.
            await using var stream = File.OpenRead(path);
            PreviewBitmap = new Bitmap(stream);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change — expected, not an error.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Title graphics preview bake failed.");
        }
        finally
        {
            if (!ct.IsCancellationRequested) IsPreviewRendering = false;
        }
    }

    [RelayCommand]
    private void SetLightCount(string count) => LightCount = int.TryParse(count, out var n) ? Math.Clamp(n, 1, 3) : LightCount;

    // ── Preview panel ──────────────────────────────────────────────────────
    // Width derived from the REAL, currently measured category-card aspect ratio
    // (CardDimensionInfo — live, since it depends on window size) so the preview pane
    // actually resembles the real cards' shape. Clamped to a max of 0.6 (width/height)
    // rather than trusting the raw measurement outright — if CardDimensionInfo hasn't
    // measured anything yet, or ever reports something less extreme than the real
    // card, this floor still guarantees a visibly tall/portrait box instead of
    // something that reads as barely-rectangular.
    private const double PreviewMaxWidth = 180;
    private const double PreviewMaxAspect = 0.6; // width/height — lower = taller

    private static double CurrentCardAspect
    {
        get
        {
            var size = CardDimensionInfo.CategoryCardPixelSize;
            var measured = size.Width > 0 && size.Height > 0 ? size.Width / size.Height : PreviewMaxAspect;
            return Math.Min(measured, PreviewMaxAspect);
        }
    }

    public double PreviewPaneWidth => PreviewMaxWidth;
    public double PreviewPaneHeight => PreviewMaxWidth / CurrentCardAspect;

    public double PreviewTranslateX => PositionX / 100.0 * PreviewPaneWidth;
    public double PreviewTranslateY => PositionY / 100.0 * PreviewPaneHeight;

    // ── Color-wheel sub-panel ─────────────────────────────────────────────
    // Field keys — a plain string tag rather than an enum, since it's only ever used
    // as a dictionary-style switch key here and as a CommandParameter in the View.
    [ObservableProperty] private string? _editingColorKey;
    public bool IsEditingColor => EditingColorKey is not null;

    // [ObservableProperty] only auto-raises change notification for EditingColorKey
    // itself — IsEditingColor/EditingColorLabel are separate computed properties that
    // need their own notification, or the picker-panel visibility binding never
    // re-evaluates and the panel silently never appears.
    partial void OnEditingColorKeyChanged(string? value)
    {
        OnPropertyChanged(nameof(IsEditingColor));
        OnPropertyChanged(nameof(EditingColorLabel));
    }

    public string EditingColorLabel => EditingColorKey switch
    {
        "FillTop" => "Fill — Top",
        "FillMid" => "Fill — Mid",
        "FillBottom" => "Fill — Bottom",
        "Bevel" => "Bevel",
        "Outline" => "Outline / Depth",
        "LightMain" => "Main Light",
        "LightKey1" => "Key Light 1",
        "LightKey2" => "Key Light 2",
        _ => string.Empty,
    };

    [ObservableProperty] private double _pickerHue = 50;        // 0-360
    [ObservableProperty] private double _pickerSaturation = 1;  // 0-1
    [ObservableProperty] private double _pickerValue = 1;       // 0-1 (brightness)

    private bool _syncingPickerFromHex;
    private Action<string>? _applyPickerColor;

    public string PickerHex => ColorToHex(HsvToRgb(PickerHue, PickerSaturation, PickerValue));
    public IBrush PickerPreviewBrush => new SolidColorBrush(HsvToRgb(PickerHue, PickerSaturation, PickerValue));

    [RelayCommand]
    private void SelectColor(string key) => BeginEditColor(key);

    private void BeginEditColor(string key)
    {
        var currentHex = key switch
        {
            "FillTop" => FillTopColor,
            "FillMid" => FillMidColor,
            "FillBottom" => FillBottomColor,
            "Bevel" => BevelColor,
            "Outline" => OutlineColor,
            "LightMain" => LightMainColor,
            "LightKey1" => LightKey1Color,
            "LightKey2" => LightKey2Color,
            _ => "#FFFFFF",
        };

        _applyPickerColor = key switch
        {
            "FillTop" => v => FillTopColor = v,
            "FillMid" => v => FillMidColor = v,
            "FillBottom" => v => FillBottomColor = v,
            "Bevel" => v => BevelColor = v,
            "Outline" => v => OutlineColor = v,
            "LightMain" => v => LightMainColor = v,
            "LightKey1" => v => LightKey1Color = v,
            "LightKey2" => v => LightKey2Color = v,
            _ => null,
        };

        _syncingPickerFromHex = true;
        try
        {
            var (h, s, v) = RgbToHsv(ParseColorSafe(currentHex));
            PickerHue = h;
            PickerSaturation = s;
            PickerValue = v;
        }
        finally { _syncingPickerFromHex = false; }

        EditingColorKey = key; // triggers OnEditingColorKeyChanged, which raises both notifications
        PickerFocusIndex = 0;
    }

    partial void OnPickerHueChanged(double value) => OnPickerHsvChanged();
    partial void OnPickerSaturationChanged(double value) => OnPickerHsvChanged();
    partial void OnPickerValueChanged(double value) => OnPickerHsvChanged();

    private void OnPickerHsvChanged()
    {
        if (_syncingPickerFromHex) return;
        OnPropertyChanged(nameof(PickerHex));
        OnPropertyChanged(nameof(PickerPreviewBrush));
        _applyPickerColor?.Invoke(PickerHex);
    }

    [RelayCommand]
    private void CloseColorPicker() => EditingColorKey = null;

    /// <summary>Called by ConfigEditorViewModel.TryHandleContentBack — gives the
    /// picker sub-mode first crack at Back, same as the sub-mode patterns in other
    /// tabs (Games/Audio/Hooks/Systems). Returns true if it was open and got closed.</summary>
    public bool TryExitColorPicker()
    {
        if (!IsEditingColor) return false;
        EditingColorKey = null;
        return true;
    }

    [ObservableProperty] private int _pickerFocusIndex; // 0=Hue,1=Saturation,2=Value,3=Done
    private const int PickerFieldCount = 4;

    public bool IsPickerHueFocused        => PickerFocusIndex == 0;
    public bool IsPickerSaturationFocused => PickerFocusIndex == 1;
    public bool IsPickerValueFocused      => PickerFocusIndex == 2;
    public bool IsPickerDoneFocused       => PickerFocusIndex == 3;

    partial void OnPickerFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsPickerHueFocused));
        OnPropertyChanged(nameof(IsPickerSaturationFocused));
        OnPropertyChanged(nameof(IsPickerValueFocused));
        OnPropertyChanged(nameof(IsPickerDoneFocused));
    }

    // ── Field highlight (main panel) ──────────────────────────────────────
    [ObservableProperty] private int _focusIndex;
    // Enabled, FillTop, FillMid, FillBottom, Bevel, Outline,
    // LightCount, LightMain, LightKey1, LightKey2,
    // Scale, PositionX, PositionY, RotationX, RotationY, RotationZ, Save
    private const int PositionCount = 17;

    public bool IsEnabledFocused    => FocusIndex == 0;
    public bool IsFillTopFocused    => FocusIndex == 1;
    public bool IsFillMidFocused    => FocusIndex == 2;
    public bool IsFillBottomFocused => FocusIndex == 3;
    public bool IsBevelFocused      => FocusIndex == 4;
    public bool IsOutlineFocused    => FocusIndex == 5;
    public bool IsLightCountFocused => FocusIndex == 6;
    public bool IsLightMainFocused  => FocusIndex == 7;
    public bool IsLightKey1Focused  => FocusIndex == 8;
    public bool IsLightKey2Focused  => FocusIndex == 9;
    public bool IsScaleFocused      => FocusIndex == 10;
    public bool IsPositionXFocused  => FocusIndex == 11;
    public bool IsPositionYFocused  => FocusIndex == 12;
    public bool IsRotationXFocused  => FocusIndex == 13;
    public bool IsRotationYFocused  => FocusIndex == 14;
    public bool IsRotationZFocused  => FocusIndex == 15;
    public bool IsSaveFocused       => FocusIndex == 16;

    partial void OnFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsEnabledFocused));
        OnPropertyChanged(nameof(IsFillTopFocused));
        OnPropertyChanged(nameof(IsFillMidFocused));
        OnPropertyChanged(nameof(IsFillBottomFocused));
        OnPropertyChanged(nameof(IsBevelFocused));
        OnPropertyChanged(nameof(IsOutlineFocused));
        OnPropertyChanged(nameof(IsLightCountFocused));
        OnPropertyChanged(nameof(IsLightMainFocused));
        OnPropertyChanged(nameof(IsLightKey1Focused));
        OnPropertyChanged(nameof(IsLightKey2Focused));
        OnPropertyChanged(nameof(IsScaleFocused));
        OnPropertyChanged(nameof(IsPositionXFocused));
        OnPropertyChanged(nameof(IsPositionYFocused));
        OnPropertyChanged(nameof(IsRotationXFocused));
        OnPropertyChanged(nameof(IsRotationYFocused));
        OnPropertyChanged(nameof(IsRotationZFocused));
        OnPropertyChanged(nameof(IsSaveFocused));
    }

    // ── Controller navigation ────────────────────────────────────────────
    public void NavigateUp()
    {
        if (IsEditingColor) { PickerFocusIndex = (PickerFocusIndex - 1 + PickerFieldCount) % PickerFieldCount; return; }
        FocusIndex = (FocusIndex - 1 + PositionCount) % PositionCount;
    }

    public void NavigateDown()
    {
        if (IsEditingColor) { PickerFocusIndex = (PickerFocusIndex + 1) % PickerFieldCount; return; }
        FocusIndex = (FocusIndex + 1) % PositionCount;
    }

    public void NavigateLeft()
    {
        if (IsEditingColor)
        {
            switch (PickerFocusIndex)
            {
                case 0: PickerHue = Math.Max(0, PickerHue - 10); break;
                case 1: PickerSaturation = Math.Max(0, Math.Round(PickerSaturation - 0.05, 2)); break;
                case 2: PickerValue = Math.Max(0, Math.Round(PickerValue - 0.05, 2)); break;
            }
            return;
        }

        switch (FocusIndex)
        {
            case 6: LightCount = Math.Max(1, LightCount - 1); break;
            case 10: Scale = Math.Max(0.5, Math.Round(Scale - 0.05, 2)); break;
            case 11: PositionX = Math.Max(-50, PositionX - 2); break;
            case 12: PositionY = Math.Max(-50, PositionY - 2); break;
            case 13: RotationXDegrees = Math.Max(-45, RotationXDegrees - 1); break;
            case 14: RotationYDegrees = Math.Max(-45, RotationYDegrees - 1); break;
            case 15: RotationZDegrees = Math.Max(-15, RotationZDegrees - 1); break;
        }
    }

    public void NavigateRight()
    {
        if (IsEditingColor)
        {
            switch (PickerFocusIndex)
            {
                case 0: PickerHue = Math.Min(360, PickerHue + 10); break;
                case 1: PickerSaturation = Math.Min(1, Math.Round(PickerSaturation + 0.05, 2)); break;
                case 2: PickerValue = Math.Min(1, Math.Round(PickerValue + 0.05, 2)); break;
            }
            return;
        }

        switch (FocusIndex)
        {
            case 6: LightCount = Math.Min(3, LightCount + 1); break;
            case 10: Scale = Math.Min(2.5, Math.Round(Scale + 0.05, 2)); break;
            case 11: PositionX = Math.Min(50, PositionX + 2); break;
            case 12: PositionY = Math.Min(50, PositionY + 2); break;
            case 13: RotationXDegrees = Math.Min(45, RotationXDegrees + 1); break;
            case 14: RotationYDegrees = Math.Min(45, RotationYDegrees + 1); break;
            case 15: RotationZDegrees = Math.Min(15, RotationZDegrees + 1); break;
        }
    }

    public async Task ConfirmAsync()
    {
        if (IsEditingColor)
        {
            if (PickerFocusIndex == 3) CloseColorPicker();
            return;
        }

        switch (FocusIndex)
        {
            case 0: IsEnabled = !IsEnabled; break;
            case 1: BeginEditColor("FillTop"); break;
            case 2: BeginEditColor("FillMid"); break;
            case 3: BeginEditColor("FillBottom"); break;
            case 4: BeginEditColor("Bevel"); break;
            case 5: BeginEditColor("Outline"); break;
            case 6: NavigateRight(); break;
            case 7: BeginEditColor("LightMain"); break;
            case 8: BeginEditColor("LightKey1"); break;
            case 9: BeginEditColor("LightKey2"); break;
            case 16: await SaveAsync(); break;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = _config.Settings;
        // Full field list, not just the ones this tab cares about — see the
        // identical pattern (and the reason for it) in every other Save command
        // across Settings, e.g. CardHighlightConfigViewModel.SaveAsync.
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
            CardHighlightColor     = s.CardHighlightColor,
            CardHighlightIntensity = s.CardHighlightIntensity,
            CardHighlightStyle     = s.CardHighlightStyle,
            CardHighlightThickness = s.CardHighlightThickness,
            TitleGraphicsEnabled   = IsEnabled,
            TitleGraphicsScale     = Scale,
            TitleGraphicsPositionX = PositionX,
            TitleGraphicsPositionY = PositionY,
            TitleGraphicsFillTopColor    = FillTopColor,
            TitleGraphicsFillMidColor    = FillMidColor,
            TitleGraphicsFillBottomColor = FillBottomColor,
            TitleGraphicsBevelColor      = BevelColor,
            TitleGraphicsOutlineColor    = OutlineColor,
            TitleGraphicsLightCount      = LightCount,
            TitleGraphicsLightMainColor  = LightMainColor,
            TitleGraphicsLightKey1Color  = LightKey1Color,
            TitleGraphicsLightKey2Color  = LightKey2Color,
            TitleGraphicsRotationXDegrees = RotationXDegrees,
            TitleGraphicsRotationYDegrees = RotationYDegrees,
            TitleGraphicsRotationZDegrees = RotationZDegrees,
            HidHideEnabled         = s.HidHideEnabled,
            HidHideCliPath         = s.HidHideCliPath,
        };

        await _config.UpdateSettingsAsync(updated);
        TitleGraphicsSettings.Load(IsEnabled, Scale, PositionX, PositionY);
        PushLiveStyle();
        StatusMessage = "Title graphics saved. Re-baking category art…";
        _logger.LogInformation("Title graphics saved: enabled={Enabled} scale={Scale} x={X} y={Y}", IsEnabled, Scale, PositionX, PositionY);

        // Style changed — every previously-baked category PNG is now stale, so re-bake
        // them all in the background. Categories keep showing their current cached PNG
        // (or the live 2D fallback) until each one's re-bake completes.
        _ = RebakeAllCategoriesAsync();
    }

    private async Task RebakeAllCategoriesAsync()
    {
        try
        {
            var categories = (await _config.GetCategoriesAsync())
                .Select(c => (c.Id, c.Label));
            await _baker.RebakeAllAsync(categories);
            StatusMessage = "Title graphics saved.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Re-baking all category title graphics failed.");
            StatusMessage = "Title graphics saved (re-bake failed — showing live preview).";
        }
    }

    // ── HSV <-> RGB (plain math, deliberately not relying on any Avalonia-specific
    // HSV API — mirrors CardHighlightConfigViewModel's identical implementation) ───

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

    // Deliberately 6-digit "#RRGGBB", NOT the 8-digit "#AARRGGBB" CardHighlightConfigViewModel's
    // identical-looking helper produces — these hex strings get baked straight into
    // template.html for three.js/JS's THREE.Color to parse, and THREE.Color's hex parser only
    // recognizes 3- or 6-digit "#rgb"/"#rrggbb", not an alpha-prefixed 8-digit form. An 8-digit
    // value there silently fails to parse and every material/light falls back to white — which
    // is exactly what was happening here even though the picker's own Avalonia-side swatch
    // preview (which DOES accept 8-digit hex) looked correct throughout.
    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color ParseColorSafe(string hex)
    {
        try { return Color.Parse(hex); }
        catch { return Color.Parse("#FFFFFFFF"); }
    }
}
