using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

/// <summary>One system in the "disabled for this system" checklist.</summary>
public sealed partial class HookSystemCheckItem : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    [ObservableProperty] private bool _isDisabledForSystem;
    [ObservableProperty] private bool _isHighlighted;

    public HookSystemCheckItem(string id, string name)
    {
        Id = id;
        Name = name;
    }
}

public sealed partial class HookConfigViewModel : ObservableObject
{
    private readonly IHookSettingsRepository _hookRepo;
    private readonly IConfigurationService _config;
    private readonly ILogger<HookConfigViewModel> _logger;

    [ObservableProperty] private bool _enabledGlobally;
    [ObservableProperty] private HookToolType _toolType = HookToolType.None;
    [ObservableProperty] private string _executablePath = string.Empty;
    [ObservableProperty] private int _startupDelayMs = 500;

    /// <summary>Separate from ToolType above — DemulShooter translates lightgun aiming
    /// rather than listening for output signals, so it can run alongside either
    /// MameHooker or Hook of the Reaper. See HookSettings.DemulShooterEnabled.</summary>
    [ObservableProperty] private bool _demulShooterEnabled;
    [ObservableProperty] private string _demulShooterExecutablePath = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<HookSystemCheckItem> SystemOverrides { get; } = [];

    public string ToolTypeLabel => ToolType switch
    {
        HookToolType.MameHooker => "MameHooker",
        HookToolType.HookOfTheReaper => "Hook of the Reaper",
        _ => "None (disabled)",
    };

    public event Func<string, string[], Task<string?>>? BrowseFileRequested;

    public HookConfigViewModel(
        IHookSettingsRepository hookRepo,
        IConfigurationService config,
        ILogger<HookConfigViewModel> logger)
    {
        _hookRepo = hookRepo;
        _config = config;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var settings = await _hookRepo.GetSettingsAsync();
        EnabledGlobally = settings.EnabledGlobally;
        ToolType = settings.ToolType;
        ExecutablePath = settings.ExecutablePath;
        StartupDelayMs = settings.StartupDelayMs;
        DemulShooterEnabled = settings.DemulShooterEnabled;
        DemulShooterExecutablePath = settings.DemulShooterExecutablePath;

        var systems = await _config.GetSystemsAsync();
        var disabledIds = new HashSet<string>(settings.DisabledForSystemIds, StringComparer.OrdinalIgnoreCase);
        SystemOverrides.Clear();
        foreach (var s in systems)
            SystemOverrides.Add(new HookSystemCheckItem(s.Id, s.Name) { IsDisabledForSystem = disabledIds.Contains(s.Id) });
    }

    partial void OnToolTypeChanged(HookToolType value) => OnPropertyChanged(nameof(ToolTypeLabel));

    // ── Field highlight ───────────────────────────────────────────────────
    [ObservableProperty] private int _focusIndex;
    private const int PositionCount = 8;
    // 0 EnabledGlobally, 1 ToolType, 2 ExecutablePath (Browse), 3 StartupDelayMs,
    // 4 DemulShooterEnabled, 5 DemulShooterExecutablePath (Browse),
    // 6 SystemOverrides (enters sub-mode), 7 Save.

    [ObservableProperty] private bool _isOverridesFocused;
    [ObservableProperty] private int _selectedOverrideIndex;

    public bool IsEnabledGloballyFocused  => FocusIndex == 0;
    public bool IsToolTypeFocused         => FocusIndex == 1;
    public bool IsExecutablePathFocused   => FocusIndex == 2;
    public bool IsStartupDelayFocused     => FocusIndex == 3;
    public bool IsDemulShooterEnabledFocused => FocusIndex == 4;
    public bool IsDemulShooterPathFocused    => FocusIndex == 5;
    public bool IsOverridesFieldFocused   => FocusIndex == 6;
    public bool IsSaveFocused             => FocusIndex == 7;

    partial void OnFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsEnabledGloballyFocused));
        OnPropertyChanged(nameof(IsToolTypeFocused));
        OnPropertyChanged(nameof(IsExecutablePathFocused));
        OnPropertyChanged(nameof(IsStartupDelayFocused));
        OnPropertyChanged(nameof(IsDemulShooterEnabledFocused));
        OnPropertyChanged(nameof(IsDemulShooterPathFocused));
        OnPropertyChanged(nameof(IsOverridesFieldFocused));
        OnPropertyChanged(nameof(IsSaveFocused));
    }

    partial void OnIsOverridesFocusedChanged(bool value) => RefreshOverrideHighlight();
    partial void OnSelectedOverrideIndexChanged(int value) => RefreshOverrideHighlight();

    private void RefreshOverrideHighlight()
    {
        for (int i = 0; i < SystemOverrides.Count; i++)
            SystemOverrides[i].IsHighlighted = IsOverridesFocused && i == SelectedOverrideIndex;
    }

    // ── Controller navigation ────────────────────────────────────────────

    public void NavigateUp()
    {
        if (IsOverridesFocused)
        {
            if (SystemOverrides.Count == 0) return;
            SelectedOverrideIndex = (SelectedOverrideIndex - 1 + SystemOverrides.Count) % SystemOverrides.Count;
            return;
        }
        FocusIndex = (FocusIndex - 1 + PositionCount) % PositionCount;
    }

    public void NavigateDown()
    {
        if (IsOverridesFocused)
        {
            if (SystemOverrides.Count == 0) return;
            SelectedOverrideIndex = (SelectedOverrideIndex + 1) % SystemOverrides.Count;
            return;
        }
        FocusIndex = (FocusIndex + 1) % PositionCount;
    }

    public void NavigateLeft()
    {
        if (IsOverridesFocused) return;
        switch (FocusIndex)
        {
            case 1: CycleToolType(-1); break;
            case 3: StartupDelayMs = Math.Max(0, StartupDelayMs - 100); break;
        }
    }

    public void NavigateRight()
    {
        if (IsOverridesFocused) return;
        switch (FocusIndex)
        {
            case 1: CycleToolType(1); break;
            case 3: StartupDelayMs = Math.Min(5000, StartupDelayMs + 100); break;
        }
    }

    private void CycleToolType(int delta)
    {
        var values = Enum.GetValues<HookToolType>();
        int idx = Array.IndexOf(values, ToolType);
        idx = (idx + delta + values.Length) % values.Length;
        ToolType = values[idx];
    }

    public async Task ConfirmAsync()
    {
        if (IsOverridesFocused)
        {
            if (SelectedOverrideIndex >= 0 && SelectedOverrideIndex < SystemOverrides.Count)
            {
                var item = SystemOverrides[SelectedOverrideIndex];
                item.IsDisabledForSystem = !item.IsDisabledForSystem;
            }
            return;
        }

        switch (FocusIndex)
        {
            case 0: EnabledGlobally = !EnabledGlobally; break;
            case 2: await BrowseExecutableAsync(); break;
            case 4: DemulShooterEnabled = !DemulShooterEnabled; break;
            case 5: await BrowseDemulShooterExecutableAsync(); break;
            case 6:
                if (SystemOverrides.Count > 0)
                {
                    IsOverridesFocused = true;
                    SelectedOverrideIndex = Math.Clamp(SelectedOverrideIndex, 0, SystemOverrides.Count - 1);
                }
                break;
            case 7: await SaveAsync(); break;
            // 1, 3 (ToolType/StartupDelay) are adjusted via Left/Right, not Confirm.
        }
    }

    /// <summary>Back while the per-system overrides sub-mode is focused exits just
    /// that, back to the flat field list — same convention as every other nested
    /// list in Settings (Categories, Audio's track list, Games' category grid).</summary>
    public bool TryExitOverrides()
    {
        if (!IsOverridesFocused) return false;
        IsOverridesFocused = false;
        return true;
    }

    [RelayCommand]
    private async Task BrowseExecutableAsync()
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke("Executable Files", ["*.exe"]);
        if (path is not null) ExecutablePath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(path);
    }

    [RelayCommand]
    private async Task BrowseDemulShooterExecutableAsync()
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke("Executable Files", ["*.exe"]);
        if (path is not null) DemulShooterExecutablePath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(path);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = new HookSettings
        {
            EnabledGlobally = EnabledGlobally,
            ToolType = ToolType,
            ExecutablePath = ExecutablePath.Trim(),
            StartupDelayMs = StartupDelayMs,
            DemulShooterEnabled = DemulShooterEnabled,
            DemulShooterExecutablePath = DemulShooterExecutablePath.Trim(),
            DisabledForSystemIds = SystemOverrides.Where(s => s.IsDisabledForSystem).Select(s => s.Id).ToList(),
        };

        await _hookRepo.SaveSettingsAsync(settings);
        StatusMessage = "Hook integration settings saved.";
        _logger.LogInformation("Hook settings saved: tool={Tool} enabled={Enabled}", ToolType, EnabledGlobally);
    }
}
