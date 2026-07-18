using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.App.ViewModels;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

public sealed partial class SystemsConfigViewModel : ObservableObject
{
    private readonly IConfigurationService _config;
    private readonly IEmulatorRepository _emulatorRepo;
    private readonly VirtualKeyboardViewModel _virtualKeyboard;
    private readonly ILogger<SystemsConfigViewModel> _logger;

    public ObservableCollection<GameSystem> Systems   { get; } = [];
    public ObservableCollection<Emulator>  Emulators { get; } = [];

    [ObservableProperty] private GameSystem? _selectedSystem;
    [ObservableProperty] private Emulator?   _selectedEmulator;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Tab state ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isSystemsTabActive = true;
    [ObservableProperty] private bool _isEmulatorsTabActive;

    // System editor fields
    [ObservableProperty] private string _editSystemId   = string.Empty;
    [ObservableProperty] private string _editSystemName = string.Empty;
    [ObservableProperty] private string _editSystemBezelPath = string.Empty;
    [ObservableProperty] private bool _isSystemEditorOpen;

    // Emulator editor fields
    [ObservableProperty] private string _editEmulatorId         = string.Empty;
    [ObservableProperty] private string _editEmulatorName       = string.Empty;
    [ObservableProperty] private bool   _editEmulatorIsRetroArch;
    [ObservableProperty] private string _editEmulatorPath       = string.Empty;
    [ObservableProperty] private string _editEmulatorArgs       = string.Empty;
    [ObservableProperty] private string _editEmulatorSystems    = string.Empty;
    [ObservableProperty] private string _editEmulatorNotes      = string.Empty;
    [ObservableProperty] private bool   _isEmulatorEditorOpen;
    [ObservableProperty] private bool   _editEmulatorExeFound;
    [ObservableProperty] private string _editEmulatorExeStatus  = string.Empty;

    // RetroArch-specific fields (visible when EditEmulatorIsRetroArch = true)
    [ObservableProperty] private string _editRetroArchExePath   = string.Empty;
    [ObservableProperty] private int    _editRunAheadFrames;
    [ObservableProperty] private bool   _editRunAheadSecondInstance;
    [ObservableProperty] private string _editShaderPresetPath   = string.Empty;
    [ObservableProperty] private string _editAdditionalConfig   = string.Empty;

    // BIOS files required by this emulator — applies to every game launched
    // through it. A specific game can override this via Game.BiosOverridePaths.
    public ObservableCollection<string> EditEmulatorBiosPaths { get; } = [];
    [ObservableProperty] private bool _isBiosListFocused;
    [ObservableProperty] private int _selectedBiosIndex;

    // ── System editor field highlight ────────────────────────────────────
    [ObservableProperty] private int _systemEditorFocusIndex;
    private const int SystemEditorPositionCount = 5; // Id, Name, BezelPath, Save, Cancel

    public bool IsSystemIdFocused     => SystemEditorFocusIndex == 0;
    public bool IsSystemNameFocused   => SystemEditorFocusIndex == 1;
    public bool IsSystemBezelPathFocused => SystemEditorFocusIndex == 2;
    public bool IsSaveSystemFocused   => SystemEditorFocusIndex == 3;
    public bool IsCancelSystemFocused => SystemEditorFocusIndex == 4;

    partial void OnSystemEditorFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSystemIdFocused));
        OnPropertyChanged(nameof(IsSystemNameFocused));
        OnPropertyChanged(nameof(IsSystemBezelPathFocused));
        OnPropertyChanged(nameof(IsSaveSystemFocused));
        OnPropertyChanged(nameof(IsCancelSystemFocused));
    }

    // ── Emulator editor field highlight ──────────────────────────────────
    // Field order changes depending on RetroArch mode (different fields are shown),
    // so positions are looked up by name via GetEmulatorFieldOrder() rather than
    // fixed numbers — this keeps navigation correct regardless of which fields are
    // currently visible.
    [ObservableProperty] private int _emulatorEditorFocusIndex;

    private string[] GetEmulatorFieldOrder() => EditEmulatorIsRetroArch
        ? ["Id", "Name", "RetroArchToggle", "Path", "RetroArchExePath", "RunAheadFrames",
           "RunAheadSecondInstance", "ShaderPresetPath", "AdditionalConfig",
           "BiosList", "AddBios", "SupportedSystems", "Notes", "Save", "Cancel"]
        : ["Id", "Name", "RetroArchToggle", "Path", "Args",
           "BiosList", "AddBios", "SupportedSystems", "Notes", "Save", "Cancel"];

    private string CurrentEmulatorField
    {
        get
        {
            var order = GetEmulatorFieldOrder();
            return EmulatorEditorFocusIndex >= 0 && EmulatorEditorFocusIndex < order.Length
                ? order[EmulatorEditorFocusIndex] : string.Empty;
        }
    }

    public bool IsEmuIdFocused                 => CurrentEmulatorField == "Id";
    public bool IsEmuNameFocused               => CurrentEmulatorField == "Name";
    public bool IsEmuRetroArchToggleFocused    => CurrentEmulatorField == "RetroArchToggle";
    public bool IsEmuPathFocused                => CurrentEmulatorField == "Path";
    public bool IsEmuArgsFocused                => CurrentEmulatorField == "Args";
    public bool IsEmuRetroArchExePathFocused    => CurrentEmulatorField == "RetroArchExePath";
    public bool IsEmuRunAheadFramesFocused      => CurrentEmulatorField == "RunAheadFrames";
    public bool IsEmuRunAheadSecondInstanceFocused => CurrentEmulatorField == "RunAheadSecondInstance";
    public bool IsEmuShaderPresetPathFocused    => CurrentEmulatorField == "ShaderPresetPath";
    public bool IsEmuAdditionalConfigFocused    => CurrentEmulatorField == "AdditionalConfig";
    public bool IsEmuBiosListFocused             => CurrentEmulatorField == "BiosList";
    public bool IsEmuAddBiosFocused               => CurrentEmulatorField == "AddBios";
    public bool IsEmuSupportedSystemsFocused    => CurrentEmulatorField == "SupportedSystems";
    public bool IsEmuNotesFocused                => CurrentEmulatorField == "Notes";
    public bool IsEmuSaveFocused                 => CurrentEmulatorField == "Save";
    public bool IsEmuCancelFocused               => CurrentEmulatorField == "Cancel";

    partial void OnEmulatorEditorFocusIndexChanged(int value) => RaiseAllEmuFieldFocusChanged();

    partial void OnEditEmulatorIsRetroArchChanged(bool value)
    {
        EmulatorEditorFocusIndex = 0; // field order changed — reset to a valid position
        RaiseAllEmuFieldFocusChanged();
    }

    private void RaiseAllEmuFieldFocusChanged()
    {
        OnPropertyChanged(nameof(IsEmuIdFocused));
        OnPropertyChanged(nameof(IsEmuNameFocused));
        OnPropertyChanged(nameof(IsEmuRetroArchToggleFocused));
        OnPropertyChanged(nameof(IsEmuPathFocused));
        OnPropertyChanged(nameof(IsEmuArgsFocused));
        OnPropertyChanged(nameof(IsEmuRetroArchExePathFocused));
        OnPropertyChanged(nameof(IsEmuRunAheadFramesFocused));
        OnPropertyChanged(nameof(IsEmuRunAheadSecondInstanceFocused));
        OnPropertyChanged(nameof(IsEmuShaderPresetPathFocused));
        OnPropertyChanged(nameof(IsEmuAdditionalConfigFocused));
        OnPropertyChanged(nameof(IsEmuBiosListFocused));
        OnPropertyChanged(nameof(IsEmuAddBiosFocused));
        OnPropertyChanged(nameof(IsEmuSupportedSystemsFocused));
        OnPropertyChanged(nameof(IsEmuNotesFocused));
        OnPropertyChanged(nameof(IsEmuSaveFocused));
        OnPropertyChanged(nameof(IsEmuCancelFocused));
    }

    // ── Controller navigation ────────────────────────────────────────────
    // At the top level: browses whichever internal sub-tab (Systems or Emulators) is
    // active; Left/Right switches between the two sub-tabs. Once an editor is open,
    // Up/Down instead moves the field highlight within that editor; Confirm opens the
    // virtual keyboard for text fields, toggles checkboxes, or triggers Save/Cancel.
    public void NavigateUp()
    {
        if (IsSystemEditorOpen)
        {
            SystemEditorFocusIndex = (SystemEditorFocusIndex - 1 + SystemEditorPositionCount) % SystemEditorPositionCount;
            return;
        }
        if (IsBiosListFocused)
        {
            if (EditEmulatorBiosPaths.Count == 0) return;
            SelectedBiosIndex = (SelectedBiosIndex - 1 + EditEmulatorBiosPaths.Count) % EditEmulatorBiosPaths.Count;
            return;
        }
        if (IsEmulatorEditorOpen)
        {
            int count = GetEmulatorFieldOrder().Length;
            EmulatorEditorFocusIndex = (EmulatorEditorFocusIndex - 1 + count) % count;
            return;
        }
        if (IsSystemsTabActive) MoveSystemSelection(-1);
        else MoveEmulatorSelection(-1);
    }

    public void NavigateDown()
    {
        if (IsSystemEditorOpen)
        {
            SystemEditorFocusIndex = (SystemEditorFocusIndex + 1) % SystemEditorPositionCount;
            return;
        }
        if (IsBiosListFocused)
        {
            if (EditEmulatorBiosPaths.Count == 0) return;
            SelectedBiosIndex = (SelectedBiosIndex + 1) % EditEmulatorBiosPaths.Count;
            return;
        }
        if (IsEmulatorEditorOpen)
        {
            int count = GetEmulatorFieldOrder().Length;
            EmulatorEditorFocusIndex = (EmulatorEditorFocusIndex + 1) % count;
            return;
        }
        if (IsSystemsTabActive) MoveSystemSelection(1);
        else MoveEmulatorSelection(1);
    }

    public void NavigateLeft()
    {
        if (IsBiosListFocused) return;
        if (IsEmulatorEditorOpen && CurrentEmulatorField == "RunAheadFrames")
        {
            EditRunAheadFrames = Math.Max(0, EditRunAheadFrames - 1);
            return;
        }
        if (IsSystemEditorOpen || IsEmulatorEditorOpen) return;
        SelectSystemsTab();
    }

    public void NavigateRight()
    {
        if (IsBiosListFocused) return;
        if (IsEmulatorEditorOpen && CurrentEmulatorField == "RunAheadFrames")
        {
            EditRunAheadFrames = Math.Min(10, EditRunAheadFrames + 1);
            return;
        }
        if (IsSystemEditorOpen || IsEmulatorEditorOpen) return;
        SelectEmulatorsTab();
    }

    public async Task ConfirmAsync()
    {
        if (IsSystemEditorOpen)
        {
            switch (SystemEditorFocusIndex)
            {
                case 0: _virtualKeyboard.Open("System Id", EditSystemId, v => EditSystemId = v); break;
                case 1: _virtualKeyboard.Open("Display Name", EditSystemName, v => EditSystemName = v); break;
                case 2: await BrowseSystemBezelAsync(); break;
                case 3: await SaveSystemAsync(); break;
                case 4: CancelSystemEditor(); break;
            }
            return;
        }

        if (IsBiosListFocused)
        {
            if (SelectedBiosIndex >= 0 && SelectedBiosIndex < EditEmulatorBiosPaths.Count)
            {
                EditEmulatorBiosPaths.RemoveAt(SelectedBiosIndex);
                if (EditEmulatorBiosPaths.Count == 0) IsBiosListFocused = false;
                else SelectedBiosIndex = Math.Clamp(SelectedBiosIndex, 0, EditEmulatorBiosPaths.Count - 1);
            }
            return;
        }

        if (IsEmulatorEditorOpen)
        {
            switch (CurrentEmulatorField)
            {
                case "Id": _virtualKeyboard.Open("Emulator Id", EditEmulatorId, v => EditEmulatorId = v); break;
                case "Name": _virtualKeyboard.Open("Display Name", EditEmulatorName, v => EditEmulatorName = v); break;
                case "RetroArchToggle": EditEmulatorIsRetroArch = !EditEmulatorIsRetroArch; break;
                case "Path": _virtualKeyboard.Open(
                    EditEmulatorIsRetroArch ? "Core Library Path" : "Executable Path",
                    EditEmulatorPath, v => EditEmulatorPath = v); break;
                case "Args": _virtualKeyboard.Open("Launch Arguments", EditEmulatorArgs, v => EditEmulatorArgs = v); break;
                case "RetroArchExePath": _virtualKeyboard.Open("RetroArch Executable", EditRetroArchExePath, v => EditRetroArchExePath = v); break;
                case "RunAheadSecondInstance": EditRunAheadSecondInstance = !EditRunAheadSecondInstance; break;
                case "ShaderPresetPath": _virtualKeyboard.Open("Shader Preset", EditShaderPresetPath, v => EditShaderPresetPath = v); break;
                case "AdditionalConfig": _virtualKeyboard.Open("Additional Config", EditAdditionalConfig, v => EditAdditionalConfig = v); break;
                case "BiosList":
                    if (EditEmulatorBiosPaths.Count > 0)
                    {
                        IsBiosListFocused = true;
                        SelectedBiosIndex = Math.Clamp(SelectedBiosIndex, 0, EditEmulatorBiosPaths.Count - 1);
                    }
                    break;
                case "AddBios": await BrowseAddBiosAsync(); break;
                case "SupportedSystems": _virtualKeyboard.Open("Supported Systems", EditEmulatorSystems, v => EditEmulatorSystems = v); break;
                case "Notes": _virtualKeyboard.Open("Notes", EditEmulatorNotes, v => EditEmulatorNotes = v); break;
                case "Save": await SaveEmulatorAsync(); break;
                case "Cancel": CancelEmulatorEditor(); break;
                // RunAheadFrames is numeric — adjusted via Left/Right, not Confirm.
            }
            return;
        }

        if (IsSystemsTabActive) EditSelectedSystem();
        else EditSelectedEmulator();
    }

    /// <summary>Back while the BIOS list sub-mode is focused exits just that, back to
    /// the flat field list — same convention as every other nested list in Settings
    /// (Categories, Audio's track list, Games' category grid).</summary>
    public bool TryExitBiosList()
    {
        if (!IsBiosListFocused) return false;
        IsBiosListFocused = false;
        return true;
    }

    private void MoveSystemSelection(int delta)
    {
        if (Systems.Count == 0) return;
        int idx = SelectedSystem is null ? 0 : Systems.IndexOf(SelectedSystem);
        idx = (idx + delta + Systems.Count) % Systems.Count;
        SelectedSystem = Systems[idx];
    }

    private void MoveEmulatorSelection(int delta)
    {
        if (Emulators.Count == 0) return;
        int idx = SelectedEmulator is null ? 0 : Emulators.IndexOf(SelectedEmulator);
        idx = (idx + delta + Emulators.Count) % Emulators.Count;
        SelectedEmulator = Emulators[idx];
    }

    partial void OnEditEmulatorPathChanged(string value)
    {
        var resolved = string.IsNullOrWhiteSpace(value) ? string.Empty
            : Path.IsPathRooted(value) ? value
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, value));

        EditEmulatorExeFound  = !string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved);
        EditEmulatorExeStatus = EditEmulatorExeFound ? "✔ Found" : "✘ Not found";
    }

    public event Func<string, string[], Task<string?>>? BrowseFileRequested;

    public SystemsConfigViewModel(
        IConfigurationService config,
        IEmulatorRepository emulatorRepo,
        VirtualKeyboardViewModel virtualKeyboard,
        ILogger<SystemsConfigViewModel> logger)
    {
        _config = config;
        _emulatorRepo = emulatorRepo;
        _virtualKeyboard = virtualKeyboard;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var systems = await _config.GetSystemsAsync();
        Systems.Clear();
        foreach (var s in systems) Systems.Add(s);

        var emulators = await _emulatorRepo.GetAllAsync();
        Emulators.Clear();
        foreach (var e in emulators) Emulators.Add(e);
    }

    // ── Tab switching ──────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectSystemsTab()
    {
        IsSystemsTabActive   = true;
        IsEmulatorsTabActive = false;
    }

    [RelayCommand]
    private void SelectEmulatorsTab()
    {
        IsSystemsTabActive   = false;
        IsEmulatorsTabActive = true;
    }

    /// <summary>Dedicated LB/RB sub-tab switching (Systems ↔ Emulators), same reasoning
    /// as AudioConfigViewModel's equivalent — always available, separate from
    /// Left/Right which is overloaded to adjust values in an open editor.</summary>
    public void SwitchSubTabLeft() => SelectSystemsTab();
    public void SwitchSubTabRight() => SelectEmulatorsTab();

    // ── System commands ────────────────────────────────────────────────────

    [RelayCommand]
    private void AddNewSystem()
    {
        EditSystemId = string.Empty;
        EditSystemName = string.Empty;
        EditSystemBezelPath = string.Empty;
        IsSystemEditorOpen = true;
    }

    [RelayCommand]
    private void EditSelectedSystem()
    {
        if (SelectedSystem is null) return;
        EditSystemId   = SelectedSystem.Id;
        EditSystemName = SelectedSystem.Name;
        EditSystemBezelPath = SelectedSystem.BezelPath;
        IsSystemEditorOpen = true;
    }

    [RelayCommand]
    private async Task BrowseSystemBezelAsync()
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke("Bezel Images", ["*.png", "*.jpg", "*.jpeg", "*.*"]);
        if (path is not null) EditSystemBezelPath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(path);
    }

    [RelayCommand]
    private async Task SaveSystemAsync()
    {
        if (string.IsNullOrWhiteSpace(EditSystemId) || string.IsNullOrWhiteSpace(EditSystemName))
        {
            StatusMessage = "System Id and Name are required.";
            return;
        }

        var system = new GameSystem
        {
            Id = EditSystemId.Trim().ToLowerInvariant(),
            Name = EditSystemName.Trim(),
            BezelPath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(EditSystemBezelPath.Trim()),
        };
        await _config.AddOrUpdateSystemAsync(system);

        var idx = Systems.IndexOf(Systems.FirstOrDefault(s => s.Id == system.Id)!);
        if (idx >= 0) Systems[idx] = system; else Systems.Add(system);

        IsSystemEditorOpen = false;
        StatusMessage = $"System '{system.Name}' saved.";
    }

    [RelayCommand]
    private async Task DeleteSelectedSystemAsync()
    {
        if (SelectedSystem is null) return;
        await _config.DeleteSystemAsync(SelectedSystem.Id);
        Systems.Remove(SelectedSystem);
        SelectedSystem = null;
        StatusMessage = "System deleted.";
    }

    [RelayCommand]
    private void CancelSystemEditor() => IsSystemEditorOpen = false;

    // ── Emulator commands ──────────────────────────────────────────────────

    [RelayCommand]
    private void AddNewEmulator()
    {
        EditEmulatorId = string.Empty;
        EditEmulatorName = string.Empty;
        EditEmulatorIsRetroArch = false;
        EditEmulatorPath = string.Empty;
        EditEmulatorArgs = string.Empty;
        EditEmulatorSystems = string.Empty;
        EditEmulatorNotes = string.Empty;
        EditRetroArchExePath = string.Empty;
        EditRunAheadFrames = 0;
        EditRunAheadSecondInstance = false;
        EditShaderPresetPath = string.Empty;
        EditAdditionalConfig = string.Empty;
        EditEmulatorBiosPaths.Clear();
        IsBiosListFocused = false;
        SelectedBiosIndex = 0;
        IsEmulatorEditorOpen = true;
    }

    [RelayCommand]
    private void EditSelectedEmulator()
    {
        if (SelectedEmulator is null) return;
        EditEmulatorId          = SelectedEmulator.Id;
        EditEmulatorName        = SelectedEmulator.Name;
        EditEmulatorIsRetroArch = SelectedEmulator.IsRetroArchCore;
        EditEmulatorPath        = SelectedEmulator.ExecutablePath;
        EditEmulatorArgs        = SelectedEmulator.Arguments;
        EditEmulatorSystems     = SelectedEmulator.SupportedSystems;
        EditEmulatorNotes       = SelectedEmulator.Notes;

        var ra = SelectedEmulator.RetroArch;
        EditRetroArchExePath        = ra?.RetroArchExePath       ?? string.Empty;
        EditRunAheadFrames          = ra?.RunAheadFrames         ?? 0;
        EditRunAheadSecondInstance  = ra?.RunAheadSecondInstance ?? false;
        EditShaderPresetPath        = ra?.ShaderPresetPath       ?? string.Empty;
        EditAdditionalConfig        = ra?.AdditionalConfig       ?? string.Empty;

        EditEmulatorBiosPaths.Clear();
        foreach (var bios in SelectedEmulator.BiosPaths) EditEmulatorBiosPaths.Add(bios);
        IsBiosListFocused = false;
        SelectedBiosIndex = 0;

        IsEmulatorEditorOpen = true;
    }

    [RelayCommand]
    private async Task BrowseAddBiosAsync()
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke("BIOS Files", ["*.bin", "*.rom", "*.img", "*.*"]);
        if (path is not null)
            EditEmulatorBiosPaths.Add(UGL.Core.Utilities.PortablePathHelper.ToPortablePath(path));
    }

    [RelayCommand]
    private void RemoveBios(string path)
    {
        var idx = EditEmulatorBiosPaths.IndexOf(path);
        EditEmulatorBiosPaths.Remove(path);
        if (EditEmulatorBiosPaths.Count > 0)
            SelectedBiosIndex = Math.Clamp(idx, 0, EditEmulatorBiosPaths.Count - 1);
        else
            IsBiosListFocused = false;
    }

    [RelayCommand]
    private async Task BrowseEmulatorPathAsync()
    {
        if (BrowseFileRequested is null) return;
        var label = EditEmulatorIsRetroArch ? "RetroArch Core Library (.dll)" : "Emulator Executable";
        var patterns = EditEmulatorIsRetroArch ? new[] { "*.dll" } : new[] { "*.exe", "*.*" };
        var path = await BrowseFileRequested.Invoke(label, patterns);
        if (path is not null) EditEmulatorPath = path;
    }

    [RelayCommand]
    private async Task BrowseRetroArchExeAsync()
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke("RetroArch Executable", ["*.exe"]);
        if (path is not null) EditRetroArchExePath = path;
    }

    [RelayCommand]
    private async Task BrowseShaderPresetAsync()
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke("Shader Preset", ["*.glslp", "*.slangp", "*.cgp", "*.*"]);
        if (path is not null) EditShaderPresetPath = path;
    }

    [RelayCommand]
    private async Task SaveEmulatorAsync()
    {
        if (string.IsNullOrWhiteSpace(EditEmulatorId) || string.IsNullOrWhiteSpace(EditEmulatorName))
        {
            StatusMessage = "Emulator Id and Name are required.";
            return;
        }

        var emulator = new Emulator
        {
            Id               = EditEmulatorId.Trim().ToLowerInvariant(),
            Name             = EditEmulatorName.Trim(),
            IsRetroArchCore  = EditEmulatorIsRetroArch,
            // Stored relative to the app's own folder when possible, so this keeps
            // working if the whole portable install moves to a different drive
            // letter or machine — ProcessEmulatorLauncher.ResolveExecutablePath
            // already resolves either form back to an absolute path when launching.
            ExecutablePath   = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(EditEmulatorPath.Trim()),
            Arguments        = EditEmulatorArgs.Trim(),
            SupportedSystems = EditEmulatorSystems.Trim(),
            Notes            = EditEmulatorNotes.Trim(),
            BiosPaths        = EditEmulatorBiosPaths.ToList(),
            RetroArch        = EditEmulatorIsRetroArch ? new RetroArchConfig
            {
                RetroArchExePath        = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(EditRetroArchExePath.Trim()),
                RunAheadFrames          = EditRunAheadFrames,
                RunAheadSecondInstance  = EditRunAheadSecondInstance,
                ShaderPresetPath        = EditShaderPresetPath.Trim(),
                AdditionalConfig        = EditAdditionalConfig.Trim(),
            } : null,
        };

        await _emulatorRepo.AddOrUpdateAsync(emulator);
        await _emulatorRepo.SaveAsync();

        var idx = Emulators.IndexOf(Emulators.FirstOrDefault(e => e.Id == emulator.Id)!);
        if (idx >= 0) Emulators[idx] = emulator; else Emulators.Add(emulator);

        IsEmulatorEditorOpen = false;
        StatusMessage = $"Emulator '{emulator.Name}' saved.";
    }

    [RelayCommand]
    private async Task DeleteSelectedEmulatorAsync()
    {
        if (SelectedEmulator is null) return;
        await _emulatorRepo.DeleteAsync(SelectedEmulator.Id);
        await _emulatorRepo.SaveAsync();
        Emulators.Remove(SelectedEmulator);
        SelectedEmulator = null;
        StatusMessage = "Emulator deleted.";
    }

    [RelayCommand]
    private void CancelEmulatorEditor() => IsEmulatorEditorOpen = false;
}
