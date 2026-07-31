using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

/// <summary>
/// Backing VM for the Peripheral Hooks configuration tab.
/// Shows all detected HID devices with their type, connection status,
/// and player index assignment. Allows reassignment and re-scan.
/// </summary>
public sealed partial class PeripheralConfigViewModel : ObservableObject
{
    private readonly IPeripheralRegistry _registry;
    private readonly IRawInputService _rawInput;
    private readonly IConfigurationService _config;
    private readonly ILogger<PeripheralConfigViewModel> _logger;

    public ObservableCollection<RawInputDevice> Devices { get; } = [];

    [ObservableProperty] private RawInputDevice? _selectedDevice;
    [ObservableProperty] private int _editPlayerIndex;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    // ── HidHide (real, OS-level version of Game.DisabledDeviceTypes — see
    // ProcessEmulatorLauncher/IHidHideService) ──────────────────────────────────
    [ObservableProperty] private bool _hidHideEnabled;
    [ObservableProperty] private string _hidHideCliPath = string.Empty;

    public event Func<string, string[], Task<string?>>? BrowseFileRequested;

    /// <summary>
    /// Unified controller-navigation position: -3 = HidHide enabled toggle,
    /// -2 = HidHide CLI path, -1 = the Rescan button, 0..N-1 = an index into Devices.
    /// Having Rescan/HidHide fields share the same position sequence as the device
    /// list (rather than being reachable only via mouse) keeps every field in this
    /// tab controller-navigable — see IsRescanFocused etc.
    /// </summary>
    [ObservableProperty] private int _focusIndex = -3;

    public bool IsHidHideEnabledFocused => FocusIndex == -3;
    public bool IsHidHideCliPathFocused => FocusIndex == -2;
    public bool IsRescanFocused => FocusIndex == -1;

    public List<int> PlayerIndexOptions { get; } = [0, 1, 2, 3, 4];

    public PeripheralConfigViewModel(
        IPeripheralRegistry registry,
        IRawInputService rawInput,
        IConfigurationService config,
        ILogger<PeripheralConfigViewModel> logger)
    {
        _registry = registry;
        _rawInput = rawInput;
        _config = config;
        _logger = logger;
    }

    public Task InitializeAsync()
    {
        HidHideEnabled = _config.Settings.HidHideEnabled;
        HidHideCliPath = _config.Settings.HidHideCliPath;
        RefreshDeviceList();
        return Task.CompletedTask;
    }

    private async Task SaveHidHideSettingsAsync()
    {
        // Full field list, not just the two this tab owns — see the SDS §13 note on
        // AppSettings reconstructions silently dropping fields left out here.
        var s = _config.Settings;
        var updated = new AppSettings
        {
            MediaRootPath = s.MediaRootPath, RomsRootPath = s.RomsRootPath,
            EmulatorsRootPath = s.EmulatorsRootPath, AddonsRootPath = s.AddonsRootPath,
            LogsRootPath = s.LogsRootPath, ActiveThemeId = s.ActiveThemeId,
            DefaultCategoryId = s.DefaultCategoryId, Language = s.Language,
            TargetFrameRate = s.TargetFrameRate,
            EnableBackgroundMusic = s.EnableBackgroundMusic, EnableNavigationSounds = s.EnableNavigationSounds,
            MusicVolume = s.MusicVolume, SoundVolume = s.SoundVolume,
            SoundNavigatePath = s.SoundNavigatePath, SoundConfirmPath = s.SoundConfirmPath,
            SoundBackPath = s.SoundBackPath, SoundErrorPath = s.SoundErrorPath,
            EnableVideoPreview = s.EnableVideoPreview, VideoPreviewDelayMs = s.VideoPreviewDelayMs,
            VideoPreviewAudio = s.VideoPreviewAudio, VideoPreviewVolume = s.VideoPreviewVolume,
            CardHighlightColor = s.CardHighlightColor, CardHighlightIntensity = s.CardHighlightIntensity,
            CardHighlightStyle = s.CardHighlightStyle, CardHighlightThickness = s.CardHighlightThickness,
            HidHideEnabled = HidHideEnabled,
            HidHideCliPath = HidHideCliPath.Trim(),
        };
        await _config.UpdateSettingsAsync(updated);
    }

    partial void OnSelectedDeviceChanged(RawInputDevice? value)
    {
        EditPlayerIndex = value?.PlayerIndex ?? 0;
    }

    partial void OnFocusIndexChanged(int value)
    {
        SyncSelectedDeviceFromFocus();
        OnPropertyChanged(nameof(IsHidHideEnabledFocused));
        OnPropertyChanged(nameof(IsHidHideCliPathFocused));
        OnPropertyChanged(nameof(IsRescanFocused));
    }

    private void SyncSelectedDeviceFromFocus()
        => SelectedDevice = FocusIndex >= 0 && FocusIndex < Devices.Count ? Devices[FocusIndex] : null;

    // ── Controller navigation (mirrors FilterOverlayViewModel/ConfigEditorViewModel) ──
    // Manipulates the same bound properties the XAML already reflects — no separate
    // visual wiring needed, same trick as the Settings sidebar menu. Positions 0-2 are
    // HidHideEnabled/HidHideCliPath/Rescan; positions 3..N+2 are Devices[0..N-1]
    // (see ToPosition/FromPosition).

    private int PositionCount => Devices.Count + 3;
    private int ToPosition(int focusIndex) => focusIndex + 3;
    private int FromPosition(int position) => position - 3;

    public void NavigateUp()
    {
        int pos = ToPosition(FocusIndex);
        pos = (pos - 1 + PositionCount) % PositionCount;
        FocusIndex = FromPosition(pos);
    }

    public void NavigateDown()
    {
        int pos = ToPosition(FocusIndex);
        pos = (pos + 1) % PositionCount;
        FocusIndex = FromPosition(pos);
    }

    public void NavigateLeft()
        => EditPlayerIndex = Math.Max(EditPlayerIndex - 1, PlayerIndexOptions.Min());

    public void NavigateRight()
        => EditPlayerIndex = Math.Min(EditPlayerIndex + 1, PlayerIndexOptions.Max());

    /// <summary>
    /// A/Select while this tab has content focus. Rescans if Rescan is the highlighted
    /// row, otherwise assigns the highlighted device to the currently dialed-in player
    /// index — same action the Assign button performs.
    /// </summary>
    public async Task ConfirmAsync()
    {
        if (IsHidHideEnabledFocused)
        {
            HidHideEnabled = !HidHideEnabled;
            await SaveHidHideSettingsAsync();
            return;
        }

        if (IsHidHideCliPathFocused)
        {
            if (BrowseFileRequested is null) return;
            var path = await BrowseFileRequested.Invoke("HidHideCLI.exe", ["*.exe"]);
            if (path is not null)
            {
                HidHideCliPath = path;
                await SaveHidHideSettingsAsync();
            }
            return;
        }

        if (IsRescanFocused)
        {
            await RescanDevicesAsync();
            // After a scan, land on the first device (if any) for convenience.
            if (Devices.Count > 0) FocusIndex = 0;
            return;
        }

        if (SelectedDevice is not null)
            await AssignPlayerIndexAsync();
    }

    [RelayCommand]
    private async Task RescanDevicesAsync()
    {
        IsLoading = true;
        try
        {
            var connected = await Task.Run(() => _rawInput.EnumerateDevices());
            _registry.MergeConnectedDevices(connected);
            await _registry.SaveAsync();
            RefreshDeviceList();
            StatusMessage = $"Scan complete — {connected.Count} device(s) found.";
            _logger.LogInformation("Peripheral rescan: {Count} devices.", connected.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Peripheral rescan failed.");
            StatusMessage = $"Scan failed: {ex.GetType().Name} — {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task AssignPlayerIndexAsync()
    {
        if (SelectedDevice is null) return;
        await _registry.AssignPlayerIndexAsync(SelectedDevice.HardwarePath, EditPlayerIndex);
        SelectedDevice.PlayerIndex = EditPlayerIndex;
        StatusMessage = EditPlayerIndex == 0
            ? $"'{SelectedDevice.FriendlyName}' unassigned."
            : $"'{SelectedDevice.FriendlyName}' assigned to Player {EditPlayerIndex}.";
        RefreshDeviceList();
    }

    [RelayCommand]
    private async Task RemoveDeviceAsync()
    {
        if (SelectedDevice is null) return;
        await _registry.RemoveDeviceAsync(SelectedDevice.HardwarePath);
        StatusMessage = $"'{SelectedDevice.FriendlyName}' removed from registry.";
        RefreshDeviceList();
    }

    private void RefreshDeviceList()
    {
        Devices.Clear();
        foreach (var d in _registry.KnownDevices
                     .Where(d => d.DeviceType != RawInputDeviceType.Keyboard)
                     .OrderBy(d => d.PlayerIndex)
                     .ThenBy(d => d.FriendlyName))
        {
            Devices.Add(d);
        }

        if (FocusIndex >= Devices.Count)
            FocusIndex = Devices.Count - 1; // clamps to Rescan (-1) if the list is now empty

        // Devices are freshly re-fetched instances even when FocusIndex itself didn't
        // numerically change, so SelectedDevice must be re-synced explicitly here too.
        SyncSelectedDeviceFromFocus();
    }
}
