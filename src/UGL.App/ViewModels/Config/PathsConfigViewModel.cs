using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

/// <summary>
/// Settings → Paths tab.
///
/// Root paths are defaults — any path can point to any drive or
/// network share. Individual systems can override the ROM path.
///
/// Resolution order for ROMs:
///   1. Game.RomPath (absolute) → used directly
///   2. Game.RomPath (relative) → resolved against system ROM path or global root
///   3. GameSystem.RomPath     → per-system override (any drive)
///   4. {RomsRootPath}\{systemId}\ → global default
/// </summary>
public sealed partial class PathsConfigViewModel : ObservableObject
{
    private readonly IConfigurationService _config;
    private readonly ILogger<PathsConfigViewModel> _logger;

    // ── Global root paths ──────────────────────────────────────────────────
    [ObservableProperty] private string _mediaRootPath     = string.Empty;
    [ObservableProperty] private string _romsRootPath      = string.Empty;
    [ObservableProperty] private string _emulatorsRootPath = string.Empty;
    [ObservableProperty] private string _addonsRootPath    = string.Empty;
    [ObservableProperty] private string _logsRootPath      = string.Empty;

    // ── Per-system ROM path overrides ──────────────────────────────────────
    public ObservableCollection<GameSystem> Systems { get; } = [];
    [ObservableProperty] private GameSystem? _selectedSystem;
    [ObservableProperty] private string _editSystemRomPath = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>
    /// Controller-navigation position: 0-4 = the five root-path rows, 5 = the system
    /// ROM override row (Left/Right cycles SelectedSystem), 6 = Save System Path,
    /// 7 = Save All Paths.
    /// </summary>
    [ObservableProperty] private int _focusIndex;
    private const int PositionCount = 8;

    // ── Field highlight (same Classes-binding trick used everywhere else) ──────────
    public bool IsMediaRootFocused      => FocusIndex == 0;
    public bool IsRomsRootFocused       => FocusIndex == 1;
    public bool IsEmulatorsRootFocused  => FocusIndex == 2;
    public bool IsAddonsRootFocused     => FocusIndex == 3;
    public bool IsLogsRootFocused       => FocusIndex == 4;
    public bool IsSaveSystemFocused     => FocusIndex == 6;
    public bool IsSaveAllFocused        => FocusIndex == 7;

    partial void OnFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsMediaRootFocused));
        OnPropertyChanged(nameof(IsRomsRootFocused));
        OnPropertyChanged(nameof(IsEmulatorsRootFocused));
        OnPropertyChanged(nameof(IsAddonsRootFocused));
        OnPropertyChanged(nameof(IsLogsRootFocused));
        OnPropertyChanged(nameof(IsSaveSystemFocused));
        OnPropertyChanged(nameof(IsSaveAllFocused));
    }

    public event Func<string, Task<string?>>? BrowseFolderRequested;

    public PathsConfigViewModel(
        IConfigurationService config,
        ILogger<PathsConfigViewModel> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var s = _config.Settings;
        MediaRootPath     = s.MediaRootPath;
        RomsRootPath      = s.RomsRootPath;
        EmulatorsRootPath = s.EmulatorsRootPath;
        AddonsRootPath    = s.AddonsRootPath;
        LogsRootPath      = s.LogsRootPath;

        var systems = await _config.GetSystemsAsync();
        Systems.Clear();
        foreach (var sys in systems.OrderBy(s => s.Name))
            Systems.Add(sys);

        SelectedSystem = Systems.FirstOrDefault();
    }

    partial void OnSelectedSystemChanged(GameSystem? value)
        => EditSystemRomPath = value?.RomPath ?? string.Empty;

    // ── Controller navigation ────────────────────────────────────────────
    public void NavigateUp() => FocusIndex = (FocusIndex - 1 + PositionCount) % PositionCount;
    public void NavigateDown() => FocusIndex = (FocusIndex + 1) % PositionCount;

    public void NavigateLeft()
    {
        if (FocusIndex != 5 || Systems.Count == 0) return;
        int idx = SelectedSystem is null ? 0 : Systems.IndexOf(SelectedSystem);
        idx = (idx - 1 + Systems.Count) % Systems.Count;
        SelectedSystem = Systems[idx];
    }

    public void NavigateRight()
    {
        if (FocusIndex != 5 || Systems.Count == 0) return;
        int idx = SelectedSystem is null ? 0 : Systems.IndexOf(SelectedSystem);
        idx = (idx + 1) % Systems.Count;
        SelectedSystem = Systems[idx];
    }

    public async Task ConfirmAsync()
    {
        switch (FocusIndex)
        {
            case 0: await BrowseMediaRootAsync(); break;
            case 1: await BrowseRomsRootAsync(); break;
            case 2: await BrowseEmulatorsRootAsync(); break;
            case 3: await BrowseAddonsRootAsync(); break;
            case 4: await BrowseLogsRootAsync(); break;
            case 5: await BrowseSystemRomPathAsync(); break;
            case 6: await SaveSystemRomPathAsync(); break;
            case 7: await SaveAllPathsAsync(); break;
        }
    }

    // ── Browse commands ────────────────────────────────────────────────────

    [RelayCommand] private async Task BrowseMediaRootAsync()
        => MediaRootPath = await BrowseAsync("Select Media Root Folder") ?? MediaRootPath;

    [RelayCommand] private async Task BrowseRomsRootAsync()
        => RomsRootPath = await BrowseAsync("Select ROMs Root Folder") ?? RomsRootPath;

    [RelayCommand] private async Task BrowseEmulatorsRootAsync()
        => EmulatorsRootPath = await BrowseAsync("Select Emulators Root Folder") ?? EmulatorsRootPath;

    [RelayCommand] private async Task BrowseAddonsRootAsync()
        => AddonsRootPath = await BrowseAsync("Select Addons Root Folder") ?? AddonsRootPath;

    [RelayCommand] private async Task BrowseLogsRootAsync()
        => LogsRootPath = await BrowseAsync("Select Logs Folder") ?? LogsRootPath;

    [RelayCommand] private async Task BrowseSystemRomPathAsync()
        => EditSystemRomPath = await BrowseAsync($"Select ROM folder for {SelectedSystem?.Name ?? "system"}") ?? EditSystemRomPath;

    private async Task<string?> BrowseAsync(string title)
    {
        if (BrowseFolderRequested is null) return null;
        return await BrowseFolderRequested.Invoke(title);
    }

    // ── Save ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveSystemRomPathAsync()
    {
        if (SelectedSystem is null) return;
        SelectedSystem.RomPath = EditSystemRomPath.Trim();
        await _config.AddOrUpdateSystemAsync(SelectedSystem);
        StatusMessage = $"ROM path saved for '{SelectedSystem.Name}'.";
        _logger.LogInformation("System ROM path updated: {Id} → {Path}",
            SelectedSystem.Id, SelectedSystem.RomPath);
    }

    [RelayCommand]
    private async Task SaveAllPathsAsync()
    {
        var s = _config.Settings;
        s.MediaRootPath     = MediaRootPath.Trim();
        s.RomsRootPath      = RomsRootPath.Trim();
        s.EmulatorsRootPath = EmulatorsRootPath.Trim();
        s.AddonsRootPath    = AddonsRootPath.Trim();
        s.LogsRootPath      = LogsRootPath.Trim();

        await _config.SaveSettingsAsync();
        StatusMessage = "Paths saved.";
        _logger.LogInformation("Root paths saved.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the ROM directory for a given system using the priority order:
    /// GameSystem.RomPath → {RomsRootPath}\{systemId}
    /// </summary>
    public string ResolveSystemRomPath(GameSystem system)
    {
        if (!string.IsNullOrWhiteSpace(system.RomPath))
            return system.RomPath;

        var root = Path.IsPathRooted(RomsRootPath)
            ? RomsRootPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, RomsRootPath));

        return Path.Combine(root, system.Id);
    }
}
