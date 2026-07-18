using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

public sealed partial class ThemeConfigViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private readonly IConfigurationService _config;
    private readonly ILogger<ThemeConfigViewModel> _logger;

    public ObservableCollection<Theme> AvailableThemes { get; } = [];

    [ObservableProperty] private Theme? _selectedTheme;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ThemeConfigViewModel(
        IThemeService themeService,
        IConfigurationService config,
        ILogger<ThemeConfigViewModel> logger)
    {
        _themeService = themeService;
        _config = config;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var themes = await _themeService.GetAvailableThemesAsync();
        AvailableThemes.Clear();
        foreach (var t in themes) AvailableThemes.Add(t);

        SelectedTheme = AvailableThemes.FirstOrDefault(
            t => t.Id == _themeService.ActiveTheme.Id)
            ?? AvailableThemes.FirstOrDefault();
    }

    // ── Controller navigation ────────────────────────────────────────────
    // Browses the theme list; Confirm applies the highlighted theme.
    public void NavigateUp()
    {
        if (AvailableThemes.Count == 0) return;
        int idx = SelectedTheme is null ? 0 : AvailableThemes.IndexOf(SelectedTheme);
        idx = (idx - 1 + AvailableThemes.Count) % AvailableThemes.Count;
        SelectedTheme = AvailableThemes[idx];
    }

    public void NavigateDown()
    {
        if (AvailableThemes.Count == 0) return;
        int idx = SelectedTheme is null ? 0 : AvailableThemes.IndexOf(SelectedTheme);
        idx = (idx + 1) % AvailableThemes.Count;
        SelectedTheme = AvailableThemes[idx];
    }

    public void NavigateLeft() { }
    public void NavigateRight() { }

    public async Task ConfirmAsync() => await ApplyThemeAsync();

    [RelayCommand]
    private async Task ApplyThemeAsync()
    {
        if (SelectedTheme is null) return;

        await _themeService.ApplyThemeAsync(SelectedTheme.Id);

        // Persist the selection to settings.json
        var s = _config.Settings;
        var updated = new AppSettings
        {
            MediaRootPath          = s.MediaRootPath,
            RomsRootPath           = s.RomsRootPath,
            ActiveThemeId          = SelectedTheme.Id,
            DefaultCategoryId      = s.DefaultCategoryId,
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
            TargetFrameRate        = s.TargetFrameRate,
            Language               = s.Language,
        };

        await _config.UpdateSettingsAsync(updated);
        StatusMessage = $"Theme '{SelectedTheme.Name}' applied.";
        _logger.LogInformation("Theme applied: {Id}", SelectedTheme.Id);
    }
}
