using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

/// <summary>
/// Settings → Title Graphics tab. Configures the category-card title-text
/// overlay — on/off, and where it sits vertically on the card. The text itself
/// is always the category's own Label (see CategoryCardViewModel), not
/// something this tab lets you override.
/// </summary>
public sealed partial class TitleGraphicsConfigViewModel : ObservableObject
{
    private readonly IConfigurationService _config;
    private readonly ILogger<TitleGraphicsConfigViewModel> _logger;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _placement = "Middle"; // "Top" | "Middle" | "Bottom"
    [ObservableProperty] private string _statusMessage = string.Empty;

    public bool IsTopSelected    => Placement == "Top";
    public bool IsMiddleSelected => Placement == "Middle";
    public bool IsBottomSelected => Placement == "Bottom";

    public TitleGraphicsConfigViewModel(
        IConfigurationService config,
        ILogger<TitleGraphicsConfigViewModel> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task InitializeAsync()
    {
        var s = _config.Settings;
        IsEnabled = s.TitleGraphicsEnabled;
        Placement = s.TitleGraphicsPlacement;
        TitleGraphicsSettings.Load(IsEnabled, Placement);
        return Task.CompletedTask;
    }

    partial void OnIsEnabledChanged(bool value) => PushLive();

    partial void OnPlacementChanged(string value)
    {
        OnPropertyChanged(nameof(IsTopSelected));
        OnPropertyChanged(nameof(IsMiddleSelected));
        OnPropertyChanged(nameof(IsBottomSelected));
        PushLive();
    }

    /// <summary>Updates the static live-settings bridge immediately, so any visible
    /// category card reflects the change right away — not just after Save.</summary>
    private void PushLive() => TitleGraphicsSettings.Load(IsEnabled, Placement);

    [RelayCommand]
    private void SetPlacement(string placement) => Placement = placement;

    // ── Field highlight ───────────────────────────────────────────────────
    [ObservableProperty] private int _focusIndex;
    private const int PositionCount = 3; // Enabled, Placement, Save

    public bool IsEnabledFocused   => FocusIndex == 0;
    public bool IsPlacementFocused => FocusIndex == 1;
    public bool IsSaveFocused      => FocusIndex == 2;

    partial void OnFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsEnabledFocused));
        OnPropertyChanged(nameof(IsPlacementFocused));
        OnPropertyChanged(nameof(IsSaveFocused));
    }

    // ── Controller navigation ────────────────────────────────────────────
    public void NavigateUp() => FocusIndex = (FocusIndex - 1 + PositionCount) % PositionCount;
    public void NavigateDown() => FocusIndex = (FocusIndex + 1) % PositionCount;

    private static readonly string[] Placements = ["Top", "Middle", "Bottom"];

    public void NavigateLeft()
    {
        if (FocusIndex != 1) return;
        int idx = (Array.IndexOf(Placements, Placement) - 1 + Placements.Length) % Placements.Length;
        Placement = Placements[idx];
    }

    public void NavigateRight()
    {
        if (FocusIndex != 1) return;
        int idx = (Array.IndexOf(Placements, Placement) + 1) % Placements.Length;
        Placement = Placements[idx];
    }

    public async Task ConfirmAsync()
    {
        switch (FocusIndex)
        {
            case 0: IsEnabled = !IsEnabled; break;
            case 1: NavigateRight(); break;
            case 2: await SaveAsync(); break;
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
            TitleGraphicsPlacement = Placement,
            HidHideEnabled         = s.HidHideEnabled,
            HidHideCliPath         = s.HidHideCliPath,
        };

        await _config.UpdateSettingsAsync(updated);
        TitleGraphicsSettings.Load(IsEnabled, Placement);
        StatusMessage = "Title graphics saved.";
        _logger.LogInformation("Title graphics saved: enabled={Enabled} placement={Placement}", IsEnabled, Placement);
    }
}
