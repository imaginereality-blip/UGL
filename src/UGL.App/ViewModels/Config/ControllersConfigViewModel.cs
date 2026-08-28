using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.App.Views.Controls.ControllerDiagrams;
using UGL.Core.Interfaces;
using UGL.Core.Models;
using UGL.Input;
using static UGL.Core.Models.ControllerControlDefinitions;

namespace UGL.App.ViewModels.Config;

/// <summary>
/// Settings → Controllers. Recreated from design_handoff_controller_config/README.md
/// (device-class tabs, diagram + hotspots, generic physical-control mapping list,
/// named profiles). See ControllerProfile.ControlBindings' own doc comment for how
/// this differs from ButtonMap (which remaps UGL's own menu actions) — this is a
/// generic, device-surface-first remap table applied as a translation layer in front
/// of that fixed dispatch for Gamepad, and consumed directly by RetroArch config
/// generation for Lightgun/Wheel.
///
/// Navigation follows the same list/editor + named-field-order-array + nested
/// row-browsing pattern established elsewhere in Settings (SystemsConfigViewModel's
/// field order, the old button-map sub-list) rather than inventing something new.
/// </summary>
public sealed partial class ControllersConfigViewModel : ObservableObject
{
    private readonly IControllerProfileRepository _profileRepository;
    private readonly IPeripheralRegistry _peripheralRegistry;
    private readonly IRawInputService _rawInputService;
    private readonly XInputPollingService _xInputPollingService;
    private readonly VirtualKeyboardViewModel _virtualKeyboard;
    private readonly ILogger<ControllersConfigViewModel> _logger;

    public ObservableCollection<RawInputDevice> Devices { get; } = [];
    public ObservableCollection<ControlRow> ControlRows { get; } = [];
    public ObservableCollection<ControllerProfile> SavedProfiles { get; } = [];
    public ObservableCollection<string> ImportableFiles { get; } = [];

    [ObservableProperty] private RawInputDevice? _selectedDevice;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    // ── Device-class tab (Gamepad | Lightgun | Wheel) — each remembers its own
    // selected device, per the handoff's "Tab switch" behavior. ────────────────────
    [ObservableProperty] private RawInputDeviceType _selectedDeviceClass = RawInputDeviceType.Gamepad;
    private readonly Dictionary<RawInputDeviceType, string> _lastSelectedHardwarePathByClass = new();

    public bool IsGamepadTabSelected  => SelectedDeviceClass == RawInputDeviceType.Gamepad;
    public bool IsLightgunTabSelected => SelectedDeviceClass == RawInputDeviceType.Lightgun;
    public bool IsWheelTabSelected    => SelectedDeviceClass == RawInputDeviceType.Wheel;

    private static readonly RawInputDeviceType[] DeviceClassOrder =
        [RawInputDeviceType.Gamepad, RawInputDeviceType.Lightgun, RawInputDeviceType.Wheel];

    // ── Current profile (in-memory editor state — Save persists it) ────────────────
    private ControllerProfile _currentProfile = NewDefaultProfile(RawInputDeviceType.Gamepad, string.Empty);
    public string CurrentProfileName => _currentProfile.ProfileName;

    [ObservableProperty] private WheelShifterMode _shifterMode = WheelShifterMode.Sequential;
    public string ShifterModeLabel => ShifterMode switch
    {
        WheelShifterMode.Sequential  => "Sequential",
        WheelShifterMode.HPattern    => "H-Pattern",
        _                            => "Paddles Only",
    };
    public bool IsPaddlesOnlyShifter => ShifterMode == WheelShifterMode.PaddlesOnly;
    public bool IsSequentialShifter => ShifterMode == WheelShifterMode.Sequential;
    public bool IsHPatternShifter => ShifterMode == WheelShifterMode.HPattern;

    // ── Secondary sensitivity fields (not part of the handoff's own screenshots, but
    // real, already-wired settings — ThumbDeadZone/repeat timing feed live menu
    // navigation via XInputPollingService, AxisDeadZone/Sensitivity/ForceFeedback and
    // DemulShooterTarget feed RetroArch config generation. Dropping them would be a
    // real regression, so they get a compact section below the mapping list rather
    // than disappearing. ─────────────────────────────────────────────────────────────
    [ObservableProperty] private int _thumbDeadZone = 8689;
    [ObservableProperty] private int _menuRepeatDelayMs = 400;
    [ObservableProperty] private int _menuRepeatIntervalMs = 120;
    [ObservableProperty] private string _demulShooterTarget = string.Empty;
    [ObservableProperty] private double _axisDeadZone = 0.1;
    [ObservableProperty] private double _axisSensitivity = 1.0;
    [ObservableProperty] private bool _forceFeedbackEnabled = true;

    // ── Live diagram highlight ───────────────────────────────────────────────────
    [ObservableProperty] private ushort _heldButtonMask;
    [ObservableProperty] private bool _isXInputControllerConnected;
    public IReadOnlyList<string> HighlightedControlIds { get; private set; } = [];

    private readonly DispatcherTimer _xInputConnectionPoll;

    // ── Dialogs ───────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isCaptureDialogOpen;
    [ObservableProperty] private string _captureControlLabel = string.Empty;
    [ObservableProperty] private bool _isLoadDialogOpen;
    [ObservableProperty] private bool _isImportDialogOpen;

    /// <summary>True from the moment a remap/reset/import edits _currentProfile in
    /// memory until Save (or Save As) actually writes it — see CommitBinding's remarks
    /// for why nothing applies live before that. Drives the "unsaved" marker on the
    /// profile tag so it's visible, not just true underneath.</summary>
    [ObservableProperty] private bool _hasUnsavedChanges;

    public string ProfileTagText => HasUnsavedChanges ? $"Profile: {CurrentProfileName} (unsaved)" : $"Profile: {CurrentProfileName}";
    partial void OnHasUnsavedChangesChanged(bool value) => OnPropertyChanged(nameof(ProfileTagText));
    [ObservableProperty] private int _dialogSelectedIndex;

    public ControllersConfigViewModel(
        IControllerProfileRepository profileRepository,
        IPeripheralRegistry peripheralRegistry,
        IRawInputService rawInputService,
        XInputPollingService xInputPollingService,
        VirtualKeyboardViewModel virtualKeyboard,
        ILogger<ControllersConfigViewModel> logger)
    {
        _profileRepository = profileRepository;
        _peripheralRegistry = peripheralRegistry;
        _rawInputService = rawInputService;
        _xInputPollingService = xInputPollingService;
        _virtualKeyboard = virtualKeyboard;
        _logger = logger;

        _xInputPollingService.RawButtonStateChanged += (_, mask) => { HeldButtonMask = mask; RecomputeHighlight(); };
        _rawInputService.RawInputReceived += OnRawInputReceivedForLiveHighlight;

        IsXInputControllerConnected = _xInputPollingService.IsControllerConnected;
        _xInputConnectionPoll = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => IsXInputControllerConnected = _xInputPollingService.IsControllerConnected);
        _xInputConnectionPoll.Start();
    }

    public Task InitializeAsync()
    {
        _ = RescanDevicesAsync();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RescanDevicesAsync()
    {
        IsLoading = true;
        try
        {
            var connected = await Task.Run(() => _rawInputService.EnumerateDevices());
            _peripheralRegistry.MergeConnectedDevices(connected);
            await _peripheralRegistry.SaveAsync();
            await RefreshDeviceListAsync();
            StatusMessage = $"Scan complete — {connected.Count} device(s) found.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Controller rescan failed.");
            StatusMessage = $"Scan failed: {ex.GetType().Name} — {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private async Task RefreshDeviceListAsync()
    {
        Devices.Clear();
        foreach (var d in _peripheralRegistry.KnownDevices
                     .Where(d => d.DeviceType == SelectedDeviceClass)
                     .OrderBy(d => d.DisplayLabel))
        {
            Devices.Add(d);
        }

        RawInputDevice? target = null;
        if (_lastSelectedHardwarePathByClass.TryGetValue(SelectedDeviceClass, out var lastPath))
            target = Devices.FirstOrDefault(d => d.HardwarePath.Equals(lastPath, StringComparison.OrdinalIgnoreCase));
        target ??= Devices.FirstOrDefault();

        SelectedDevice = target;
        if (target is not null) _lastSelectedHardwarePathByClass[SelectedDeviceClass] = target.HardwarePath;

        OnPropertyChanged(nameof(SelectedDevice));
        await LoadActiveProfileForSelectedDeviceAsync(announce: false);
    }

    // ── Device-class tab switching ───────────────────────────────────────────────
    public async Task SwitchDeviceClassAsync(int direction)
    {
        var idx = Array.IndexOf(DeviceClassOrder, SelectedDeviceClass);
        idx = (idx + direction + DeviceClassOrder.Length) % DeviceClassOrder.Length;
        SelectedDeviceClass = DeviceClassOrder[idx];
        OnPropertyChanged(nameof(IsGamepadTabSelected));
        OnPropertyChanged(nameof(IsLightgunTabSelected));
        OnPropertyChanged(nameof(IsWheelTabSelected));
        FieldFocusIndex = 0;
        await RefreshDeviceListAsync();
    }

    public async Task SwitchDeviceAsync(int direction)
    {
        if (Devices.Count == 0) return;
        var idx = SelectedDevice is null ? 0 : Devices.IndexOf(SelectedDevice);
        idx = (idx + direction + Devices.Count) % Devices.Count;
        SelectedDevice = Devices[idx];
        _lastSelectedHardwarePathByClass[SelectedDeviceClass] = SelectedDevice.HardwarePath;
        await LoadActiveProfileForSelectedDeviceAsync(announce: true);
    }

    /// <summary>Device change auto-loads that device's active saved profile — "Device
    /// change — loads that device's last-used profile" per the handoff.</summary>
    private async Task LoadActiveProfileForSelectedDeviceAsync(bool announce)
    {
        if (SelectedDevice is null)
        {
            _currentProfile = NewDefaultProfile(SelectedDeviceClass, string.Empty);
        }
        else
        {
            var existing = await _profileRepository.GetByHardwarePathAsync(SelectedDevice.HardwarePath);
            _currentProfile = existing ?? NewDefaultProfile(SelectedDeviceClass, SelectedDevice.HardwarePath, SelectedDevice.FriendlyName);
            if (announce && existing is not null)
                StatusMessage = $"Auto-detected profile \"{existing.ProfileName}\" loaded for {SelectedDevice.DisplayLabel}.";
        }

        LoadFieldsFromCurrentProfile();
        RebuildControlRows();
        ApplyLiveProfile();
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(CurrentProfileName));
        OnPropertyChanged(nameof(ProfileTagText));
    }

    private void LoadFieldsFromCurrentProfile()
    {
        ThumbDeadZone = _currentProfile.ThumbDeadZone;
        MenuRepeatDelayMs = _currentProfile.MenuRepeatDelayMs;
        MenuRepeatIntervalMs = _currentProfile.MenuRepeatIntervalMs;
        DemulShooterTarget = _currentProfile.DemulShooterTarget;
        AxisDeadZone = _currentProfile.AxisDeadZone;
        AxisSensitivity = _currentProfile.AxisSensitivity;
        ForceFeedbackEnabled = _currentProfile.ForceFeedbackEnabled;
        ShifterMode = Enum.TryParse<WheelShifterMode>(_currentProfile.ShifterMode, out var m) ? m : WheelShifterMode.Sequential;
        OnPropertyChanged(nameof(ShifterModeLabel));
        OnPropertyChanged(nameof(IsPaddlesOnlyShifter));
        OnPropertyChanged(nameof(IsSequentialShifter));
        OnPropertyChanged(nameof(IsHPatternShifter));
    }

    private static ControllerProfile NewDefaultProfile(RawInputDeviceType type, string hardwarePath, string friendlyName = "") => new()
    {
        HardwarePath = hardwarePath,
        DeviceType = type,
        FriendlyName = friendlyName,
        ProfileName = "Default",
    };

    // ── Control rows for the current device class + shifter mode ────────────────
    private ControlDefinition[] GetActiveControlDefinitions()
    {
        var baseControls = GetControlsForType(SelectedDeviceClass);
        if (SelectedDeviceClass != RawInputDeviceType.Wheel) return baseControls;
        return [.. baseControls, .. GetWheelShifterControls(ShifterMode)];
    }

    private void RebuildControlRows()
    {
        ControlRows.Clear();
        foreach (var def in GetActiveControlDefinitions())
            ControlRows.Add(new ControlRow(def) { BindingDisplay = DescribeBinding(ResolveBinding(def)) });

        CurrentControlIndex = 0;
        RecomputeConflicts();
        RefreshRowHighlight();
        RefreshHotspots();
        OnPropertyChanged(nameof(CurrentRow));
    }

    private string ResolveBinding(ControlDefinition def) =>
        _currentProfile.ControlBindings.TryGetValue(def.ControlId, out var b) && !string.IsNullOrEmpty(b)
            ? b
            : def.DefaultBinding;

    private static string DescribeBinding(string binding) => string.IsNullOrEmpty(binding) ? "(unbound)" : binding;

    private void RecomputeConflicts()
    {
        var byBinding = ControlRows
            .Where(r => !string.IsNullOrEmpty(ResolveBinding(r.Definition)))
            .GroupBy(r => ResolveBinding(r.Definition));
        var conflicting = new HashSet<ControlRow>();
        foreach (var group in byBinding)
            if (group.Count() > 1)
                foreach (var row in group) conflicting.Add(row);

        foreach (var row in ControlRows) row.HasConflict = conflicting.Contains(row);
    }

    // ── Hotspots / diagram wiring ─────────────────────────────────────────────────
    public IReadOnlyList<ControlHotspot> ActiveHotspots => SelectedDeviceClass switch
    {
        RawInputDeviceType.Gamepad  => GamepadHotspots.All,
        RawInputDeviceType.Lightgun => LightgunHotspots.All,
        RawInputDeviceType.Wheel    => WheelHotspots.All,
        _                           => [],
    };

    private void RefreshHotspots()
    {
        OnPropertyChanged(nameof(ActiveHotspots));
        OnPropertyChanged(nameof(ActiveCanvasSize));
    }

    /// <summary>Must match the active device class's *DiagramArt control's own
    /// declared Width/Height exactly — see ControllerHotspotOverlay.CanvasSize.</summary>
    public Avalonia.Size ActiveCanvasSize => SelectedDeviceClass switch
    {
        RawInputDeviceType.Gamepad  => new Avalonia.Size(440, 300),
        RawInputDeviceType.Lightgun => new Avalonia.Size(560, 217.1),
        RawInputDeviceType.Wheel    => new Avalonia.Size(360, 440),
        _                           => default,
    };

    /// <summary>A diagram hotspot was clicked/tapped — starts remap for that control
    /// directly (matching the handoff's "Clicking a hotspot starts remap for that
    /// control"). Mouse/touch affordance only; D-pad+A via the single control panel
    /// remains the primary, always-available path.</summary>
    public void OnHotspotClicked(string controlId)
    {
        if (IsCapturingInput || IsDialogOpen) return;
        var row = ControlRows.FirstOrDefault(r => r.Definition.ControlId == controlId);
        if (row is null) return;

        CurrentControlIndex = ControlRows.IndexOf(row);
        BeginCapture(row);
    }

    /// <summary>Button-pool binding id → XInput bit, duplicated here (UGL.App can't
    /// reference UGL.Input's internal XInput class) for the SAME reason
    /// XInputButtonNames used to duplicate it — resolving which rows are "live" for
    /// the diagram highlight. Axis-bound controls (pedals) don't live-highlight: doing
    /// that needs raw trigger/stick magnitudes, which XInputPollingService doesn't
    /// expose as a bindable value today (only the button mask) — a known, narrow gap,
    /// not silently pretended away.</summary>
    private static readonly Dictionary<string, ushort> BindingMasks = new()
    {
        ["A"] = 0x1000, ["B"] = 0x2000, ["X"] = 0x4000, ["Y"] = 0x8000,
        ["LB"] = 0x0100, ["RB"] = 0x0200,
        ["Back"] = 0x0020, ["Start"] = 0x0010,
        ["L3"] = 0x0040, ["R3"] = 0x0080,
        ["DPadUp"] = 0x0001, ["DPadDown"] = 0x0002, ["DPadLeft"] = 0x0004, ["DPadRight"] = 0x0008,
    };

    private void RecomputeHighlight()
    {
        var active = new List<string>();
        foreach (var row in ControlRows)
        {
            var binding = ResolveBinding(row.Definition);
            if (row.Definition.IsCombo)
            {
                var parts = binding.Split('+');
                if (parts.Length == 2 &&
                    BindingMasks.TryGetValue(parts[0], out var m1) && (HeldButtonMask & m1) != 0 &&
                    BindingMasks.TryGetValue(parts[1], out var m2) && (HeldButtonMask & m2) != 0)
                    active.Add(row.Definition.ControlId);
                continue;
            }

            if (BindingMasks.TryGetValue(binding, out var mask) && (HeldButtonMask & mask) != 0)
                active.Add(row.Definition.ControlId);
        }

        HighlightedControlIds = active;
        OnPropertyChanged(nameof(HighlightedControlIds));
        MaybeAutoFocusOnPress(active);
    }

    /// <summary>Live "press to identify" for Lightgun — the RawInput equivalent of
    /// RecomputeHighlight/RawButtonStateChanged above. RawInputService fires this for
    /// every RawInput device system-wide, so it's filtered down to the Lightgun tab's
    /// selected device before doing anything; capture has its own dedicated handler
    /// (OnRawInputReceivedForCapture) so this backs off entirely while a capture is
    /// in progress rather than fighting it for the same event.</summary>
    private void OnRawInputReceivedForLiveHighlight(object? sender, RawInputEvent evt)
    {
        if (SelectedDeviceClass != RawInputDeviceType.Lightgun || IsCapturingInput) return;
        if (SelectedDevice is null || !evt.HardwarePath.Equals(SelectedDevice.HardwarePath, StringComparison.OrdinalIgnoreCase)) return;

        var active = new List<string>();
        foreach (var row in ControlRows)
        {
            var binding = ResolveBinding(row.Definition);
            var isHeld = binding switch
            {
                "MouseLeft"   => (evt.ButtonsDown & 1) != 0,
                "MouseRight"  => (evt.ButtonsDown & 2) != 0,
                "MouseMiddle" => (evt.ButtonsDown & 4) != 0,
                _ when binding.StartsWith("HidUsage", StringComparison.Ordinal) &&
                       ushort.TryParse(binding.AsSpan(8), out var usage) => evt.HidUsagesHeld.Contains(usage),
                _ => false,
            };
            if (isHeld) active.Add(row.Definition.ControlId);
        }

        HighlightedControlIds = active;
        OnPropertyChanged(nameof(HighlightedControlIds));
        MaybeAutoFocusOnPress(active);
    }

    private List<string> _previouslyHighlighted = [];

    /// <summary>The "push a button, its control lights up, then remap it" flow the
    /// user asked for — a control that just transitioned from not-held to held jumps
    /// the single-control panel to it AND moves field focus onto "Rows", so Confirm
    /// immediately opens capture for the button that was just pressed, with no manual
    /// ◀/▶ browsing needed first. Only fires on the rising edge (not every tick a
    /// button stays held) so it can't fight the user browsing elsewhere while
    /// something happens to be held down, and backs off entirely during capture/a
    /// dialog since focus means something different there.</summary>
    private void MaybeAutoFocusOnPress(List<string> active)
    {
        if (IsCapturingInput || IsDialogOpen) { _previouslyHighlighted = active; return; }

        var newlyActive = active.FirstOrDefault(id => !_previouslyHighlighted.Contains(id));
        _previouslyHighlighted = active;
        if (newlyActive is null) return;

        var row = ControlRows.FirstOrDefault(r => r.Definition.ControlId == newlyActive);
        if (row is null) return;

        CurrentControlIndex = ControlRows.IndexOf(row);
        var order = GetFieldOrder();
        var rowsFieldIndex = Array.IndexOf(order, "Rows");
        if (rowsFieldIndex >= 0) FieldFocusIndex = rowsFieldIndex;
    }

    /// <summary>Pushes ThumbDeadZone/repeat timing and (Gamepad only) the physical
    /// ControlBindings translation table live — same "singleton talking to singleton,
    /// no restart needed" reasoning as before. Does NOT touch the persisted store;
    /// only Save does that.</summary>
    private void ApplyLiveProfile()
    {
        if (SelectedDeviceClass == RawInputDeviceType.Gamepad)
        {
            var live = new ControllerProfile
            {
                HardwarePath = _currentProfile.HardwarePath,
                DeviceType = RawInputDeviceType.Gamepad,
                ThumbDeadZone = ThumbDeadZone,
                MenuRepeatDelayMs = MenuRepeatDelayMs,
                MenuRepeatIntervalMs = MenuRepeatIntervalMs,
                ControlBindings = _currentProfile.ControlBindings,
            };
            _xInputPollingService.ApplyGamepadProfile(live);
            _xInputPollingService.ApplyControlBindings(live);
        }
    }

    // ── Field order / navigation (mirrors SystemsConfigViewModel's pattern) ──────
    [ObservableProperty] private int _fieldFocusIndex;
    [ObservableProperty] private int _currentControlIndex;
    [ObservableProperty] private bool _isCapturingInput;

    private string[] GetFieldOrder() => SelectedDeviceClass switch
    {
        RawInputDeviceType.Wheel =>
        [
            "DeviceClassTab", "Device", "ShifterMode", "Save", "SaveAs", "Load", "Reset", "Export", "Import",
            "Rows", "AxisDeadZone", "AxisSensitivity", "ForceFeedbackEnabled",
        ],
        RawInputDeviceType.Lightgun =>
        [
            "DeviceClassTab", "Device", "Save", "SaveAs", "Load", "Reset", "Export", "Import",
            "Rows", "DemulShooterTarget",
        ],
        _ =>
        [
            "DeviceClassTab", "Device", "Save", "SaveAs", "Load", "Reset", "Export", "Import",
            "Rows", "ThumbDeadZone", "MenuRepeatDelayMs", "MenuRepeatIntervalMs",
        ],
    };

    private string CurrentField
    {
        get
        {
            var order = GetFieldOrder();
            return order[Math.Clamp(FieldFocusIndex, 0, order.Length - 1)];
        }
    }

    public bool IsDeviceClassTabFocused => !IsCapturingInput && CurrentField == "DeviceClassTab";
    public bool IsDeviceFieldFocused    => !IsCapturingInput && CurrentField == "Device";
    public bool IsShifterModeFocused    => !IsCapturingInput && CurrentField == "ShifterMode";
    public bool IsSaveFocused           => !IsCapturingInput && CurrentField == "Save";
    public bool IsSaveAsFocused         => !IsCapturingInput && CurrentField == "SaveAs";
    public bool IsLoadFocused           => !IsCapturingInput && CurrentField == "Load";
    public bool IsResetFocused          => !IsCapturingInput && CurrentField == "Reset";
    public bool IsExportFocused         => !IsCapturingInput && CurrentField == "Export";
    public bool IsImportFocused         => !IsCapturingInput && CurrentField == "Import";
    public bool IsRowsFocused           => !IsCapturingInput && CurrentField == "Rows";
    public bool IsThumbDeadZoneFocused  => !IsCapturingInput && CurrentField == "ThumbDeadZone";
    public bool IsMenuRepeatDelayFocused    => !IsCapturingInput && CurrentField == "MenuRepeatDelayMs";
    public bool IsMenuRepeatIntervalFocused => !IsCapturingInput && CurrentField == "MenuRepeatIntervalMs";
    public bool IsDemulShooterTargetFocused => !IsCapturingInput && CurrentField == "DemulShooterTarget";
    public bool IsAxisDeadZoneFocused    => !IsCapturingInput && CurrentField == "AxisDeadZone";
    public bool IsAxisSensitivityFocused => !IsCapturingInput && CurrentField == "AxisSensitivity";
    public bool IsForceFeedbackFocused   => !IsCapturingInput && CurrentField == "ForceFeedbackEnabled";

    partial void OnFieldFocusIndexChanged(int value) => RaiseAllFieldFocusChanged();

    private void RaiseAllFieldFocusChanged()
    {
        OnPropertyChanged(nameof(IsDeviceClassTabFocused));
        OnPropertyChanged(nameof(IsDeviceFieldFocused));
        OnPropertyChanged(nameof(IsShifterModeFocused));
        OnPropertyChanged(nameof(IsSaveFocused));
        OnPropertyChanged(nameof(IsSaveAsFocused));
        OnPropertyChanged(nameof(IsLoadFocused));
        OnPropertyChanged(nameof(IsResetFocused));
        OnPropertyChanged(nameof(IsExportFocused));
        OnPropertyChanged(nameof(IsImportFocused));
        OnPropertyChanged(nameof(IsRowsFocused));
        OnPropertyChanged(nameof(IsThumbDeadZoneFocused));
        OnPropertyChanged(nameof(IsMenuRepeatDelayFocused));
        OnPropertyChanged(nameof(IsMenuRepeatIntervalFocused));
        OnPropertyChanged(nameof(IsDemulShooterTargetFocused));
        OnPropertyChanged(nameof(IsAxisDeadZoneFocused));
        OnPropertyChanged(nameof(IsAxisSensitivityFocused));
        OnPropertyChanged(nameof(IsForceFeedbackFocused));
    }

    partial void OnCurrentControlIndexChanged(int value)
    {
        RefreshRowHighlight();
        OnPropertyChanged(nameof(CurrentRow));
    }

    private void RefreshRowHighlight()
    {
        for (int i = 0; i < ControlRows.Count; i++)
            ControlRows[i].IsHighlighted = i == CurrentControlIndex;
    }

    /// <summary>The single control shown in the "one control at a time" panel that
    /// replaced the old always-visible row list — Left/Right (while the "Rows" field
    /// is focused) cycles CurrentControlIndex, Confirm starts capture for it.</summary>
    public ControlRow? CurrentRow =>
        CurrentControlIndex >= 0 && CurrentControlIndex < ControlRows.Count ? ControlRows[CurrentControlIndex] : null;

    // ── Controller navigation ────────────────────────────────────────────────────
    // Synchronous wrappers matching ConfigEditorViewModel's existing dispatch
    // signature (NavigateContentUp/Down/Left/Right are sync void, same as every
    // other tab) — the actual work is async (switching device class/device awaits a
    // rescan + profile load), fired-and-forgotten the same way RescanDevicesAsync
    // already is from InitializeAsync.
    public void NavigateUp() => _ = NavigateUpAsync();
    public void NavigateDown() => _ = NavigateDownAsync();
    public void NavigateLeft() => _ = NavigateLeftAsync();
    public void NavigateRight() => _ = NavigateRightAsync();

    /// <summary>Dedicated LB/RB device-class switching (Gamepad/Light Gun/Steering
    /// Wheel), matching Audio/Systems/Games' SwitchSubTabLeft/Right convention — a
    /// switch that's always available regardless of which field has focus, not just
    /// when "DeviceClassTab" is the current field (see NavigateLeftAsync/RightAsync).</summary>
    public void SwitchSubTabLeft() { if (!IsDialogOpen && !IsCapturingInput) _ = SwitchDeviceClassAsync(-1); }
    public void SwitchSubTabRight() { if (!IsDialogOpen && !IsCapturingInput) _ = SwitchDeviceClassAsync(1); }

    private bool IsDialogOpen => IsLoadDialogOpen || IsImportDialogOpen;

    private int DialogItemCount => IsLoadDialogOpen ? SavedProfiles.Count : IsImportDialogOpen ? ImportableFiles.Count : 0;

    private async Task NavigateUpAsync()
    {
        if (IsCapturingInput) return;
        if (IsDialogOpen) { DialogSelectedIndex = DialogItemCount == 0 ? 0 : (DialogSelectedIndex - 1 + DialogItemCount) % DialogItemCount; return; }
        var order = GetFieldOrder();
        FieldFocusIndex = (FieldFocusIndex - 1 + order.Length) % order.Length;
        await Task.CompletedTask;
    }

    private async Task NavigateDownAsync()
    {
        if (IsCapturingInput) return;
        if (IsDialogOpen) { DialogSelectedIndex = DialogItemCount == 0 ? 0 : (DialogSelectedIndex + 1) % DialogItemCount; return; }
        var order = GetFieldOrder();
        FieldFocusIndex = (FieldFocusIndex + 1) % order.Length;
        await Task.CompletedTask;
    }

    private async Task NavigateLeftAsync()
    {
        if (IsDialogOpen || IsCapturingInput) return;
        switch (CurrentField)
        {
            // No "DeviceClassTab" case: switching Gamepad/Light Gun/Steering Wheel is
            // LB/RB-only (SwitchSubTabLeft/Right below) — the stick/D-pad landing on
            // that field no longer does anything on Left/Right, only Up/Down move the
            // focus ring off it, matching the user's explicit ask that only LB/RB tab
            // between device classes.
            case "Device": await SwitchDeviceAsync(-1); break;
            case "ShifterMode": CycleShifterMode(-1); break;
            case "Rows": CycleCurrentControl(-1); break;
            case "ThumbDeadZone": ThumbDeadZone = Math.Max(0, ThumbDeadZone - 500); break;
            case "MenuRepeatDelayMs": MenuRepeatDelayMs = Math.Max(100, MenuRepeatDelayMs - 25); break;
            case "MenuRepeatIntervalMs": MenuRepeatIntervalMs = Math.Max(20, MenuRepeatIntervalMs - 10); break;
            case "AxisDeadZone": AxisDeadZone = Math.Max(0, Math.Round(AxisDeadZone - 0.02, 2)); break;
            case "AxisSensitivity": AxisSensitivity = Math.Max(0.1, Math.Round(AxisSensitivity - 0.1, 1)); break;
            case "ForceFeedbackEnabled": ForceFeedbackEnabled = !ForceFeedbackEnabled; break;
        }
    }

    private async Task NavigateRightAsync()
    {
        if (IsDialogOpen || IsCapturingInput) return;
        switch (CurrentField)
        {
            case "Device": await SwitchDeviceAsync(1); break;
            case "ShifterMode": CycleShifterMode(1); break;
            case "Rows": CycleCurrentControl(1); break;
            case "ThumbDeadZone": ThumbDeadZone = Math.Min(30000, ThumbDeadZone + 500); break;
            case "MenuRepeatDelayMs": MenuRepeatDelayMs = Math.Min(1500, MenuRepeatDelayMs + 25); break;
            case "MenuRepeatIntervalMs": MenuRepeatIntervalMs = Math.Min(500, MenuRepeatIntervalMs + 10); break;
            case "AxisDeadZone": AxisDeadZone = Math.Min(0.9, Math.Round(AxisDeadZone + 0.02, 2)); break;
            case "AxisSensitivity": AxisSensitivity = Math.Min(3.0, Math.Round(AxisSensitivity + 0.1, 1)); break;
            case "ForceFeedbackEnabled": ForceFeedbackEnabled = !ForceFeedbackEnabled; break;
        }
    }

    private void CycleCurrentControl(int direction)
    {
        if (ControlRows.Count == 0) return;
        CurrentControlIndex = (CurrentControlIndex + direction + ControlRows.Count) % ControlRows.Count;
        OnPropertyChanged(nameof(CurrentRow));
    }

    private void CycleShifterMode(int direction)
    {
        var values = new[] { WheelShifterMode.Sequential, WheelShifterMode.HPattern, WheelShifterMode.PaddlesOnly };
        var idx = (Array.IndexOf(values, ShifterMode) + direction + values.Length) % values.Length;
        ShifterMode = values[idx];
        OnPropertyChanged(nameof(ShifterModeLabel));
        OnPropertyChanged(nameof(IsPaddlesOnlyShifter));
        OnPropertyChanged(nameof(IsSequentialShifter));
        OnPropertyChanged(nameof(IsHPatternShifter));
        RebuildControlRows();
    }

    public async Task ConfirmAsync()
    {
        if (IsCapturingInput) return; // resolved only by an actual input

        if (IsLoadDialogOpen) { await LoadSelectedProfileAsync(); return; }
        if (IsImportDialogOpen) { await ImportSelectedFileAsync(); return; }

        switch (CurrentField)
        {
            case "Rows":
                if (CurrentRow is { } row) BeginCapture(row);
                break;
            case "Save": await SaveAsync(); break;
            case "SaveAs": OpenSaveAsDialog(); break;
            case "Load": await OpenLoadDialogAsync(); break;
            case "Reset": ResetToDefaults(); break;
            case "Export": await ExportAsync(); break;
            case "Import": await OpenImportDialogAsync(); break;
            case "DemulShooterTarget":
                _virtualKeyboard.Open("DemulShooter Target", DemulShooterTarget, v => DemulShooterTarget = v);
                break;
        }
    }

    /// <summary>Gives capture/dialogs first crack at Back — same TryExitX pattern
    /// every other nested sub-mode in Settings uses.</summary>
    public bool TryExitSubMode()
    {
        if (IsCapturingInput) { CancelCapture(); return true; }
        if (IsLoadDialogOpen) { IsLoadDialogOpen = false; return true; }
        if (IsImportDialogOpen) { IsImportDialogOpen = false; return true; }
        return false;
    }

    /// <summary>Force-closes every nested sub-mode and resets field focus — called
    /// when controller focus leaves this tab entirely (ConfigEditorViewModel.
    /// ExitContent), unlike TryExitSubMode's one-level-at-a-time pop for in-tab Back.</summary>
    public void ExitToTopLevel()
    {
        while (TryExitSubMode()) { }
        FieldFocusIndex = 0;
    }

    // ── Capture ───────────────────────────────────────────────────────────────────
    // Two distinct capture SOURCES depending on device class: Gamepad is an XInput
    // device, captured via XInputPollingService.RawInputCaptured (unchanged). A
    // Lightgun like the X-Gunner isn't XInput-class at all — it reports as a mouse
    // (Trigger/etc. as left/right/middle click, already parsed and working in
    // RawInputService) plus a generic HID joystick collection for its numbered
    // buttons/D-pad (HidP_GetUsages parsing — see RawInputService.ProcessRawInput's
    // own doc comment for the "not yet verified against real hardware" caveat, and
    // for why axis/joystick-mode input isn't captured here yet). _capturingViaRawInput
    // records which source is live so EndCapture unsubscribes the right one.
    private ControlRow? _capturingRow;
    private bool _capturingViaRawInput;
    private readonly HashSet<string> _comboBuffer = [];

    private void BeginCapture(ControlRow row)
    {
        _capturingRow = row;
        _comboBuffer.Clear();
        IsCapturingInput = true;
        IsCaptureDialogOpen = true;
        CaptureControlLabel = row.Label;

        _capturingViaRawInput = SelectedDeviceClass == RawInputDeviceType.Lightgun;
        if (_capturingViaRawInput)
        {
            _rawInputService.RawInputReceived += OnRawInputReceivedForCapture;
        }
        else
        {
            _xInputPollingService.RawInputCaptured += OnRawInputCaptured;
            _xInputPollingService.SuppressActionDispatch = true;
        }

        StatusMessage = row.Definition.IsCombo
            ? $"Hold both buttons for \"{row.Label}\"…"
            : $"Press an input to assign to \"{row.Label}\"…";
    }

    /// <summary>RawInputService-backed capture for non-XInput devices (Lightgun).
    /// Fires on EVERY RawInput event system-wide, so this filters to the device
    /// actually being configured before looking at its button state. Mouse-click
    /// buttons (ButtonsPressed, already parsed and working) map to "MouseLeft"/
    /// "MouseRight"/"MouseMiddle"; HID usage IDs (HidUsagesNewlyPressed) map to
    /// "HidUsage&lt;N&gt;" — N is whatever the device's own report descriptor calls
    /// it, not a guessed button number.</summary>
    private void OnRawInputReceivedForCapture(object? sender, RawInputEvent evt)
    {
        if (!IsCapturingInput || _capturingRow is null) return;
        if (SelectedDevice is null || !evt.HardwarePath.Equals(SelectedDevice.HardwarePath, StringComparison.OrdinalIgnoreCase)) return;

        string? bindingId = null;
        if ((evt.ButtonsPressed & 1) != 0) bindingId = "MouseLeft";
        else if ((evt.ButtonsPressed & 2) != 0) bindingId = "MouseRight";
        else if ((evt.ButtonsPressed & 4) != 0) bindingId = "MouseMiddle";
        else if (evt.HidUsagesNewlyPressed.Count > 0) bindingId = $"HidUsage{evt.HidUsagesNewlyPressed[0]}";

        if (bindingId is null) return;

        var row = _capturingRow;
        if (row.Definition.IsCombo)
        {
            // No Lightgun control is currently a combo — kept for consistency with
            // the XInput capture path rather than because it's reachable today.
            _comboBuffer.Add(bindingId);
            if (_comboBuffer.Count < 2) return;
            CommitBinding(row, string.Join("+", _comboBuffer.OrderBy(x => x, StringComparer.Ordinal)));
            EndCapture();
            return;
        }

        CommitBinding(row, bindingId);
        EndCapture();
    }

    /// <summary>
    /// Cancels an in-progress capture — the dialog's own Cancel button (mouse/touch)
    /// and B/Back at the ConfigEditorViewModel level (TryHandleContentBack →
    /// TryExitSubMode, see there) both route here. Deliberately NOT triggered by
    /// pressing B as a captured INPUT any more — that used to double-fire alongside
    /// the live "Back" action B already causes, a confirmed race that corrupted
    /// capture. Now every physical input, B included, commits as a normal binding;
    /// a wrong capture is fixed by simply remapping that row again.
    /// </summary>
    [RelayCommand]
    public void CancelCapture()
    {
        if (!IsCapturingInput) return;
        EndCapture();
        StatusMessage = "Remap cancelled.";
    }

    private void OnRawInputCaptured(object? sender, CapturedInput input)
    {
        if (!IsCapturingInput || _capturingRow is null) return;
        var row = _capturingRow;

        if (row.Definition.IsCombo)
        {
            if (input.IsAxis) return; // combo controls (Reverse) are button-only
            _comboBuffer.Add(input.BindingId);
            if (_comboBuffer.Count < 2) return;

            var combo = string.Join("+", _comboBuffer.OrderBy(x => x, StringComparer.Ordinal));
            CommitBinding(row, combo);
            EndCapture();
            return;
        }

        CommitBinding(row, input.BindingId);
        EndCapture();
    }

    /// <summary>Deliberately does NOT call ApplyLiveProfile — a remap only edits
    /// _currentProfile in memory. Applying it live immediately used to mean the
    /// button's real behavior (including UGL's own menu actions, for Gamepad) changed
    /// the instant you captured an input, before you ever chose to keep it; a
    /// mis-press could silently disable Back/Confirm with no way to undo short of
    /// re-capturing correctly. Now nothing takes effect until Save actually commits
    /// it (see SaveAsync/OpenSaveAsDialog) — HasUnsavedChanges tracks that gap so the
    /// UI can say so.</summary>
    private void CommitBinding(ControlRow row, string binding)
    {
        _currentProfile.ControlBindings[row.Definition.ControlId] = binding;
        row.BindingDisplay = DescribeBinding(binding);
        RecomputeConflicts();
        HasUnsavedChanges = true;

        var conflicting = ControlRows.FirstOrDefault(r => r != row && !r.Definition.IsCombo &&
            ResolveBinding(r.Definition) == binding);
        StatusMessage = conflicting is not null
            ? $"⚠ \"{row.Label}\" mapped to {binding} — also bound to \"{conflicting.Label}\". Press Save to apply."
            : $"\"{row.Label}\" mapped to {binding}. Press Save to apply.";
    }

    private void EndCapture()
    {
        if (!IsCapturingInput) return;
        if (_capturingViaRawInput)
        {
            _rawInputService.RawInputReceived -= OnRawInputReceivedForCapture;
        }
        else
        {
            _xInputPollingService.RawInputCaptured -= OnRawInputCaptured;
            _xInputPollingService.SuppressActionDispatch = false;
        }
        IsCapturingInput = false;
        IsCaptureDialogOpen = false;
        _capturingRow = null;
        _comboBuffer.Clear();
    }

    // ── Profile bar: Save / Save As / Load / Reset / Export / Import ────────────
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedDevice is null) { StatusMessage = "No device selected."; return; }
        WriteFieldsIntoCurrentProfile();
        await _profileRepository.SaveAsync(_currentProfile, setActive: true);
        ApplyLiveProfile();
        HasUnsavedChanges = false;
        StatusMessage = $"Profile \"{_currentProfile.ProfileName}\" saved.";
        _logger.LogInformation("Controller profile saved: {Name}/{Profile} ({Type}).",
            SelectedDevice.DisplayLabel, _currentProfile.ProfileName, SelectedDeviceClass);
    }

    private void WriteFieldsIntoCurrentProfile()
    {
        _currentProfile.ThumbDeadZone = ThumbDeadZone;
        _currentProfile.MenuRepeatDelayMs = MenuRepeatDelayMs;
        _currentProfile.MenuRepeatIntervalMs = MenuRepeatIntervalMs;
        _currentProfile.DemulShooterTarget = DemulShooterTarget.Trim();
        _currentProfile.AxisDeadZone = AxisDeadZone;
        _currentProfile.AxisSensitivity = AxisSensitivity;
        _currentProfile.ForceFeedbackEnabled = ForceFeedbackEnabled;
        _currentProfile.ShifterMode = ShifterMode.ToString();
        if (SelectedDevice is not null)
        {
            _currentProfile.HardwarePath = SelectedDevice.HardwarePath;
            _currentProfile.FriendlyName = SelectedDevice.FriendlyName;
            _currentProfile.DeviceType = SelectedDeviceClass;
        }
    }

    [RelayCommand]
    private void OpenSaveAsDialog()
    {
        if (SelectedDevice is null) { StatusMessage = "No device selected."; return; }
        _virtualKeyboard.Open("Profile name", string.Empty, async name =>
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0) { StatusMessage = "Profile name can't be empty."; return; }

            WriteFieldsIntoCurrentProfile();
            var copy = CloneProfile(_currentProfile);
            copy.ProfileName = trimmed;
            await _profileRepository.SaveAsync(copy, setActive: true);
            _currentProfile = copy;
            OnPropertyChanged(nameof(CurrentProfileName));
            OnPropertyChanged(nameof(ProfileTagText));
            ApplyLiveProfile();
            HasUnsavedChanges = false;
            StatusMessage = $"Profile \"{trimmed}\" saved.";
        });
    }

    [RelayCommand]
    private async Task OpenLoadDialogAsync()
    {
        if (SelectedDevice is null) { StatusMessage = "No device selected."; return; }
        var profiles = await _profileRepository.GetProfilesForDeviceAsync(SelectedDevice.HardwarePath);
        SavedProfiles.Clear();
        foreach (var p in profiles) SavedProfiles.Add(p);
        if (SavedProfiles.Count == 0) { StatusMessage = "No saved profiles for this device yet."; return; }
        DialogSelectedIndex = 0;
        IsLoadDialogOpen = true;
    }

    [RelayCommand]
    public async Task LoadSelectedProfileAsync()
    {
        if (DialogSelectedIndex < 0 || DialogSelectedIndex >= SavedProfiles.Count || SelectedDevice is null) return;
        var chosen = SavedProfiles[DialogSelectedIndex];
        await _profileRepository.SetActiveProfileAsync(SelectedDevice.HardwarePath, chosen.ProfileName);
        _currentProfile = CloneProfile(chosen);
        IsLoadDialogOpen = false;
        LoadFieldsFromCurrentProfile();
        RebuildControlRows();
        ApplyLiveProfile();
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(CurrentProfileName));
        OnPropertyChanged(nameof(ProfileTagText));
        StatusMessage = $"Profile \"{chosen.ProfileName}\" loaded.";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        if (SelectedDevice is null) return;
        _currentProfile = NewDefaultProfile(SelectedDeviceClass, SelectedDevice.HardwarePath, SelectedDevice.FriendlyName);
        _currentProfile.ProfileName = CurrentProfileName; // keep editing the same named profile, just cleared
        LoadFieldsFromCurrentProfile();
        RebuildControlRows();
        HasUnsavedChanges = true;
        StatusMessage = "Mapping reset to factory default — press Save to apply, or reload the saved profile to undo.";
    }

    // Export/Import use UGL's own config folder rather than a native file dialog —
    // an explicitly acceptable alternative per the design handoff README ("Prototype
    // toasts a path; real implementation should use a file dialog (or UGL's own path
    // config)").
    private static readonly string ExportDirectory = Path.Combine(AppContext.BaseDirectory, "config", "controllerprofile-exports");

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (SelectedDevice is null) { StatusMessage = "No device selected."; return; }
        WriteFieldsIntoCurrentProfile();
        Directory.CreateDirectory(ExportDirectory);
        var fileName = $"{SanitizeFileNamePart(SelectedDevice.FriendlyName)}-{SanitizeFileNamePart(_currentProfile.ProfileName)}.json";
        var path = Path.Combine(ExportDirectory, fileName);
        var json = System.Text.Json.JsonSerializer.Serialize(_currentProfile, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });
        await File.WriteAllTextAsync(path, json);
        StatusMessage = $"Exported to config/controllerprofile-exports/{fileName}";
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "profile" : cleaned;
    }

    [RelayCommand]
    private async Task OpenImportDialogAsync()
    {
        ImportableFiles.Clear();
        if (Directory.Exists(ExportDirectory))
            foreach (var file in Directory.EnumerateFiles(ExportDirectory, "*.json").OrderBy(f => f))
                ImportableFiles.Add(Path.GetFileName(file));

        if (ImportableFiles.Count == 0) { StatusMessage = $"No exported profiles found in config/controllerprofile-exports/."; return; }
        DialogSelectedIndex = 0;
        IsImportDialogOpen = true;
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task ImportSelectedFileAsync()
    {
        if (DialogSelectedIndex < 0 || DialogSelectedIndex >= ImportableFiles.Count) return;
        var path = Path.Combine(ExportDirectory, ImportableFiles[DialogSelectedIndex]);
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var imported = System.Text.Json.JsonSerializer.Deserialize<ControllerProfile>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
            if (imported is null) { StatusMessage = "Import failed — file could not be read."; return; }

            _currentProfile = imported;
            IsImportDialogOpen = false;
            HasUnsavedChanges = true;
            LoadFieldsFromCurrentProfile();
            RebuildControlRows();
            OnPropertyChanged(nameof(CurrentProfileName));
            OnPropertyChanged(nameof(ProfileTagText));
            StatusMessage = $"Imported \"{imported.ProfileName}\" — press Save to apply it.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Controller profile import failed for {Path}.", path);
            StatusMessage = $"Import failed: {ex.GetType().Name}.";
        }
    }

    private static ControllerProfile CloneProfile(ControllerProfile source) => new()
    {
        HardwarePath = source.HardwarePath,
        DeviceType = source.DeviceType,
        FriendlyName = source.FriendlyName,
        ProfileName = source.ProfileName,
        IsActive = source.IsActive,
        ControlBindings = new Dictionary<string, string>(source.ControlBindings),
        ShifterMode = source.ShifterMode,
        ButtonMap = new Dictionary<ControllerAction, ushort>(source.ButtonMap),
        ThumbDeadZone = source.ThumbDeadZone,
        MenuRepeatDelayMs = source.MenuRepeatDelayMs,
        MenuRepeatIntervalMs = source.MenuRepeatIntervalMs,
        DemulShooterTarget = source.DemulShooterTarget,
        AxisDeadZone = source.AxisDeadZone,
        AxisSensitivity = source.AxisSensitivity,
        ForceFeedbackEnabled = source.ForceFeedbackEnabled,
    };
}

/// <summary>One row in the physical-control mapping list.</summary>
public sealed partial class ControlRow(ControlDefinition definition) : ObservableObject
{
    public ControlDefinition Definition { get; } = definition;
    public string Label => Definition.Label;
    [ObservableProperty] private string _bindingDisplay = string.Empty;
    [ObservableProperty] private bool _isHighlighted;
    [ObservableProperty] private bool _hasConflict;
}
