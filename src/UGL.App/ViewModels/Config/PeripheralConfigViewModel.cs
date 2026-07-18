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
    private readonly ILogger<PeripheralConfigViewModel> _logger;

    public ObservableCollection<RawInputDevice> Devices { get; } = [];

    [ObservableProperty] private RawInputDevice? _selectedDevice;
    [ObservableProperty] private int _editPlayerIndex;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    /// <summary>
    /// Unified controller-navigation position: -1 = the Rescan button, 0..N-1 = an index
    /// into Devices. Having Rescan share the same position sequence as the device list
    /// (rather than being reachable only via a special-cased empty-list Confirm) means
    /// it can be visually highlighted like any other focused row — see IsRescanFocused.
    /// </summary>
    [ObservableProperty] private int _focusIndex = -1;

    public bool IsRescanFocused => FocusIndex < 0;

    public List<int> PlayerIndexOptions { get; } = [0, 1, 2, 3, 4];

    public PeripheralConfigViewModel(
        IPeripheralRegistry registry,
        IRawInputService rawInput,
        ILogger<PeripheralConfigViewModel> logger)
    {
        _registry = registry;
        _rawInput = rawInput;
        _logger = logger;
    }

    public Task InitializeAsync()
    {
        RefreshDeviceList();
        return Task.CompletedTask;
    }

    partial void OnSelectedDeviceChanged(RawInputDevice? value)
    {
        EditPlayerIndex = value?.PlayerIndex ?? 0;
    }

    partial void OnFocusIndexChanged(int value)
    {
        SyncSelectedDeviceFromFocus();
        OnPropertyChanged(nameof(IsRescanFocused));
    }

    private void SyncSelectedDeviceFromFocus()
        => SelectedDevice = FocusIndex >= 0 && FocusIndex < Devices.Count ? Devices[FocusIndex] : null;

    // ── Controller navigation (mirrors FilterOverlayViewModel/ConfigEditorViewModel) ──
    // Manipulates the same bound properties the XAML already reflects — no separate
    // visual wiring needed, same trick as the Settings sidebar menu. Position 0 in the
    // sequence is Rescan; positions 1..N are Devices[0..N-1] (see ToPosition/FromPosition).

    private int PositionCount => Devices.Count + 1;
    private int ToPosition(int focusIndex) => focusIndex + 1;
    private int FromPosition(int position) => position - 1;

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
