using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;
using UGL.Media;

namespace UGL.App.ViewModels.Config;

/// <summary>
/// Backing VM for the Games configuration tab.
/// Left panel: searchable game list.
/// Right panel: full game editor form with media browse/drag-drop.
/// </summary>
public sealed partial class GamesConfigViewModel : ObservableObject
{
    private readonly IGameRepository _gameRepo;
    private readonly IConfigurationService _config;
    private readonly IEmulatorRepository _emulatorRepo;
    private readonly IPeripheralRegistry _peripheralRegistry;
    private readonly IGameScraperService _scraperService;
    private readonly IScraperSettingsRepository _scraperSettingsRepo;
    private readonly IComfyUiClient _comfyUiClient;
    private readonly MediaAssetResolver _resolver;
    private readonly SkiaMediaCache _cache;
    private readonly VirtualKeyboardViewModel _virtualKeyboard;
    private readonly ILogger<GamesConfigViewModel> _logger;

    /// <summary>
    /// Field position while the editor is open — 0=Title, 4=Genre, 7-13=ROM/process-name/
    /// media paths (text, open the virtual keyboard), 18=Save, 19=Cancel. System(1)/Emulator(3) cycle
    /// via Left/Right, Players(5) adjusts via Left/Right, Favorite(6) toggles via Confirm.
    /// Category(2) is a multi-select checkbox grid — Confirm enters a sub-mode (see
    /// IsCategoryOptionsFocused below), matching the same "enter list, Back exits it"
    /// convention as Audio's track sub-list.
    /// </summary>
    [ObservableProperty] private int _editorFocusIndex;
    private const int EditorPositionCount = 30;

    // ── Category checkbox sub-mode — entered via Confirm at EditorFocusIndex==2 ───────
    [ObservableProperty] private bool _isCategoryOptionsFocused;
    [ObservableProperty] private int _selectedCategoryOptionIndex;

    partial void OnIsCategoryOptionsFocusedChanged(bool value) => RefreshCategoryOptionHighlight();
    partial void OnSelectedCategoryOptionIndexChanged(int value) => RefreshCategoryOptionHighlight();

    private void RefreshCategoryOptionHighlight()
    {
        for (int i = 0; i < Editor.CategoryOptions.Count; i++)
            Editor.CategoryOptions[i].IsHighlighted = IsCategoryOptionsFocused && i == SelectedCategoryOptionIndex;
    }

    partial void OnIsBrowsingDisabledDeviceTypesChanged(bool value) => RefreshDeviceTypeHighlight();
    partial void OnSelectedDeviceTypeCheckIndexChanged(int value) => RefreshDeviceTypeHighlight();

    private void RefreshDeviceTypeHighlight()
    {
        for (int i = 0; i < Editor.DisabledDeviceTypeOptions.Count; i++)
            Editor.DisabledDeviceTypeOptions[i].IsHighlighted = IsBrowsingDisabledDeviceTypes && i == SelectedDeviceTypeCheckIndex;
    }

    // ── BIOS override sub-mode — entered via Confirm at EditorFocusIndex==18 ──────────
    // Nearly always empty; only needed for the rare game that requires a BIOS
    // different from its emulator's own default list (Emulator.BiosPaths). Plain
    // strings, not a wrapper VM — the ListBox's own SelectedIndex binding drives the
    // highlight directly, same pattern as Audio's track list.
    [ObservableProperty] private bool _isBrowsingBiosOverrides;
    [ObservableProperty] private int _selectedBiosOverrideIndex;

    // ── Disabled peripherals sub-mode — entered via Confirm at EditorFocusIndex==20 ──
    // Nearly always all unchecked; only needed for the rare game where a lightgun,
    // wheel, etc. would interfere (e.g. disabling both for a fighting game).
    [ObservableProperty] private bool _isBrowsingDisabledDeviceTypes;
    [ObservableProperty] private int _selectedDeviceTypeCheckIndex;

    // ── Player device assignments — richer, per-slot alternative to disabled
    // peripherals above (Game.PlayerDeviceAssignments). Two flat "pending" fields
    // (cycled via Left/Right, same convention as System/Emulator/Players) build up
    // one entry at a time; AddAssignment commits it; the list sub-mode (entered via
    // Confirm at EditorFocusIndex==25) removes existing ones. Nearly always empty.
    [ObservableProperty] private int _pendingAssignmentPlayerIndex = 1;
    [ObservableProperty] private int _pendingAssignmentDeviceIndex;
    [ObservableProperty] private bool _isBrowsingPlayerAssignments;
    [ObservableProperty] private int _selectedPlayerAssignmentIndex;

    /// <summary>Devices available to pick from when building a pending assignment —
    /// the full known-peripheral registry (connected or not; a device you're about to
    /// plug in for a specific game is a reasonable thing to pre-configure).</summary>
    public IReadOnlyList<RawInputDevice> AssignableKnownDevices => _peripheralRegistry.KnownDevices;

    public string PendingAssignmentDeviceLabel =>
        AssignableKnownDevices.Count == 0 ? "(no known devices — scan in the Controllers tab first)"
        : AssignableKnownDevices[Math.Clamp(PendingAssignmentDeviceIndex, 0, AssignableKnownDevices.Count - 1)] is var d
            ? $"{d.FriendlyName} ({d.DeviceType})" : string.Empty;

    partial void OnIsBrowsingPlayerAssignmentsChanged(bool value) => RefreshPlayerAssignmentHighlight();
    partial void OnSelectedPlayerAssignmentIndexChanged(int value) => RefreshPlayerAssignmentHighlight();

    private void RefreshPlayerAssignmentHighlight()
    {
        for (int i = 0; i < Editor.PlayerDeviceAssignmentEntries.Count; i++)
            Editor.PlayerDeviceAssignmentEntries[i].IsHighlighted = IsBrowsingPlayerAssignments && i == SelectedPlayerAssignmentIndex;
    }

    // ── Editor field highlight (drives the same Classes-binding highlight trick used
    // everywhere else — sidebar menu, Peripheral Hooks, keyboard keys) ────────────────
    public bool IsTitleFocused          => EditorFocusIndex == 0;
    public bool IsSystemFocused         => EditorFocusIndex == 1;
    public bool IsCategoryFocused       => EditorFocusIndex == 2;
    public bool IsEmulatorFocused       => EditorFocusIndex == 3;
    public bool IsGenreFocused          => EditorFocusIndex == 4;
    public bool IsPlayersFocused        => EditorFocusIndex == 5;
    public bool IsFavoriteFocused       => EditorFocusIndex == 6;
    public bool IsRomPathFocused        => EditorFocusIndex == 7;
    public bool IsProcessNameOverrideFocused => EditorFocusIndex == 8;
    public bool IsCoverPathFocused      => EditorFocusIndex == 9;
    public bool IsBackgroundPathFocused => EditorFocusIndex == 10;
    public bool IsLogoPathFocused       => EditorFocusIndex == 11;
    public bool IsVideoPathFocused      => EditorFocusIndex == 12;
    public bool IsMarqueePathFocused    => EditorFocusIndex == 13;
    public bool IsBezelOverrideFocused    => EditorFocusIndex == 14;
    public bool IsDisplayWidthOverrideFocused     => EditorFocusIndex == 15;
    public bool IsDisplayHeightOverrideFocused    => EditorFocusIndex == 16;
    public bool IsDisplayRefreshHzOverrideFocused => EditorFocusIndex == 17;
    public bool IsBiosOverrideListFocused => EditorFocusIndex == 18;
    public bool IsAddBiosOverrideFocused  => EditorFocusIndex == 19;
    public bool IsDisabledDeviceTypesFocused => EditorFocusIndex == 20;
    public bool IsDemulShooterTargetFocused => EditorFocusIndex == 21;
    public bool IsPendingAssignmentPlayerIndexFocused => EditorFocusIndex == 22;
    public bool IsPendingAssignmentDeviceFocused      => EditorFocusIndex == 23;
    public bool IsAddAssignmentFocused                => EditorFocusIndex == 24;
    public bool IsPlayerDeviceAssignmentsFocused => EditorFocusIndex == 25;
    public bool IsSaveFocused           => EditorFocusIndex == 26;
    public bool IsCancelFocused         => EditorFocusIndex == 27;
    public bool IsScrapeFocused         => EditorFocusIndex == 28;
    public bool IsGenerateCardFocused   => EditorFocusIndex == 29;

    partial void OnEditorFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTitleFocused));
        OnPropertyChanged(nameof(IsSystemFocused));
        OnPropertyChanged(nameof(IsCategoryFocused));
        OnPropertyChanged(nameof(IsEmulatorFocused));
        OnPropertyChanged(nameof(IsGenreFocused));
        OnPropertyChanged(nameof(IsPlayersFocused));
        OnPropertyChanged(nameof(IsFavoriteFocused));
        OnPropertyChanged(nameof(IsRomPathFocused));
        OnPropertyChanged(nameof(IsProcessNameOverrideFocused));
        OnPropertyChanged(nameof(IsCoverPathFocused));
        OnPropertyChanged(nameof(IsBackgroundPathFocused));
        OnPropertyChanged(nameof(IsLogoPathFocused));
        OnPropertyChanged(nameof(IsVideoPathFocused));
        OnPropertyChanged(nameof(IsMarqueePathFocused));
        OnPropertyChanged(nameof(IsBezelOverrideFocused));
        OnPropertyChanged(nameof(IsDisplayWidthOverrideFocused));
        OnPropertyChanged(nameof(IsDisplayHeightOverrideFocused));
        OnPropertyChanged(nameof(IsDisplayRefreshHzOverrideFocused));
        OnPropertyChanged(nameof(IsBiosOverrideListFocused));
        OnPropertyChanged(nameof(IsAddBiosOverrideFocused));
        OnPropertyChanged(nameof(IsDisabledDeviceTypesFocused));
        OnPropertyChanged(nameof(IsDemulShooterTargetFocused));
        OnPropertyChanged(nameof(IsPendingAssignmentPlayerIndexFocused));
        OnPropertyChanged(nameof(IsPendingAssignmentDeviceFocused));
        OnPropertyChanged(nameof(IsAddAssignmentFocused));
        OnPropertyChanged(nameof(IsPlayerDeviceAssignmentsFocused));
        OnPropertyChanged(nameof(IsSaveFocused));
        OnPropertyChanged(nameof(IsCancelFocused));
        OnPropertyChanged(nameof(IsScrapeFocused));
        OnPropertyChanged(nameof(IsGenerateCardFocused));
    }

    // ── Game list ──────────────────────────────────────────────────────────

    public ObservableCollection<Game> AllGames { get; } = [];
    public ObservableCollection<Game> FilteredGames { get; } = [];

    [ObservableProperty] private Game? _selectedGame;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Editor form ────────────────────────────────────────────────────────

    public GameEditViewModel Editor { get; } = new();

    // ── Dropdown sources ───────────────────────────────────────────────────

    public ObservableCollection<GameSystem>  Systems    { get; } = [];
    public ObservableCollection<Category>   Categories { get; } = [];
    public ObservableCollection<Emulator>   Emulators  { get; } = [];
    public ObservableCollection<string>     Genres     { get; } = [];
    public List<int> PlayerCounts { get; } = [1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>Raised when a file browse dialog is needed. The View handles the actual dialog.</summary>
    public event Func<string, string[], Task<string?>>? BrowseFileRequested;

    /// <summary>Raised when media has been copied and the game list should refresh in the browser.</summary>
    public event Action? GameCatalogChanged;

    public GamesConfigViewModel(
        IGameRepository gameRepo,
        IConfigurationService config,
        IEmulatorRepository emulatorRepo,
        IPeripheralRegistry peripheralRegistry,
        IGameScraperService scraperService,
        IScraperSettingsRepository scraperSettingsRepo,
        IComfyUiClient comfyUiClient,
        MediaAssetResolver resolver,
        SkiaMediaCache cache,
        VirtualKeyboardViewModel virtualKeyboard,
        ILogger<GamesConfigViewModel> logger)
    {
        _gameRepo = gameRepo;
        _config = config;
        _emulatorRepo = emulatorRepo;
        _peripheralRegistry = peripheralRegistry;
        _scraperService = scraperService;
        _scraperSettingsRepo = scraperSettingsRepo;
        _comfyUiClient = comfyUiClient;
        _resolver = resolver;
        _cache = cache;
        _virtualKeyboard = virtualKeyboard;
        _logger = logger;

        CardDimensionInfo.Changed += () => OnPropertyChanged(nameof(RecommendedCoverSizeHint));

        // Avalonia's ComboBox SelectedValue/SelectedValueBinding combination has known
        // reliability issues (unreliable on programmatic changes, double-fires on init) —
        // so System/Emulator bind via SelectedItem to a real object instead, the same
        // proven-reliable pattern already used for the Audio tab's category-override
        // combo. This subscription keeps that computed object in sync whenever the
        // underlying Id changes from code (e.g. CycleSystem/CycleEmulator, or loading
        // a different game into the editor).
        Editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GameEditViewModel.SelectedSystemId))
                OnPropertyChanged(nameof(SelectedSystemObject));
            else if (args.PropertyName == nameof(GameEditViewModel.SelectedEmulatorId))
                OnPropertyChanged(nameof(SelectedEmulatorObject));
        };
    }

    /// <summary>Bind ComboBoxes to this (SelectedItem) rather than Editor.SelectedSystemId
    /// directly via SelectedValue/SelectedValueBinding — see the constructor note above.</summary>
    public GameSystem? SelectedSystemObject
    {
        get => Systems.FirstOrDefault(s => s.Id == Editor.SelectedSystemId);
        set => Editor.SelectedSystemId = value?.Id ?? string.Empty;
    }

    /// <summary>Same reasoning as SelectedSystemObject above.</summary>
    public Emulator? SelectedEmulatorObject
    {
        get => Emulators.FirstOrDefault(e => e.Id == Editor.SelectedEmulatorId);
        set => Editor.SelectedEmulatorId = value?.Id ?? string.Empty;
    }

    /// <summary>
    /// The actual on-screen size of a game card, in real device pixels, measured live
    /// from a rendered GameCard (accounts for the current window size and display
    /// scaling — there's no single fixed "best" resolution since that depends on the
    /// user's actual screen). Empty until at least one game card has been shown once
    /// (i.e. the Game Browser has been entered at least once this session).
    /// </summary>
    public string RecommendedCoverSizeHint
    {
        get
        {
            var size = CardDimensionInfo.GameCardPixelSize;
            return size.Width > 0 && size.Height > 0
                ? $"Recommended cover size: {size.Width:F0}×{size.Height:F0}px (based on your current window)"
                : "Recommended size unknown yet — browse to a category's games once to detect it";
        }
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var games = await _gameRepo.GetAllGamesAsync();
            AllGames.Clear();
            foreach (var g in games.OrderBy(g => g.Title)) AllGames.Add(g);
            ApplySearch();

            var systems = await _config.GetSystemsAsync();
            Systems.Clear();
            foreach (var s in systems) Systems.Add(s);

            var categories = await _config.GetCategoriesAsync();
            Categories.Clear();
            foreach (var c in categories) Categories.Add(c);
            // Favorites is excluded here — its membership is computed from Game.IsFavorite,
            // not manual checkbox assignment, so it has no place in this list.
            Editor.SyncCategoryOptions(Categories.Where(c => !string.Equals(c.Id, "favorites", StringComparison.OrdinalIgnoreCase)));

            var emulators = await _emulatorRepo.GetAllAsync();
            Emulators.Clear();
            foreach (var e in emulators) Emulators.Add(e);

            // Build genre list from existing games
            Genres.Clear();
            foreach (var genre in AllGames.Select(g => g.Genre)
                                          .Where(g => !string.IsNullOrWhiteSpace(g))
                                          .Distinct().Order())
                Genres.Add(genre);
        }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// Re-loads the category list and re-syncs the editor's category checkboxes.
    /// Called when Settings closes, so a category added/renamed/removed in the
    /// Categories tab is reflected here without needing to reopen Settings.
    /// </summary>
    public async Task RefreshCategoriesAsync()
    {
        var categories = await _config.GetCategoriesAsync();
        Categories.Clear();
        foreach (var c in categories) Categories.Add(c);
        Editor.SyncCategoryOptions(Categories.Where(c => !string.Equals(c.Id, "favorites", StringComparison.OrdinalIgnoreCase)));
    }

    // ── Search ─────────────────────────────────────────────────────────────

    partial void OnSearchTextChanged(string value) => ApplySearch();

    // ── Controller navigation ────────────────────────────────────────────
    // Browses the (search-filtered) game list when the editor is closed. Once the editor
    // is open, Up/Down instead moves EditorFocusIndex between fields; Confirm on a text
    // field opens the virtual keyboard, on Save/Cancel triggers those actions directly.
    public void NavigateUp()
    {
        if (IsCategoryOptionsFocused)
        {
            if (Editor.CategoryOptions.Count == 0) return;
            SelectedCategoryOptionIndex = (SelectedCategoryOptionIndex - 1 + Editor.CategoryOptions.Count) % Editor.CategoryOptions.Count;
            return;
        }

        if (IsBrowsingBiosOverrides)
        {
            if (Editor.BiosOverridePaths.Count == 0) return;
            SelectedBiosOverrideIndex = (SelectedBiosOverrideIndex - 1 + Editor.BiosOverridePaths.Count) % Editor.BiosOverridePaths.Count;
            return;
        }

        if (IsBrowsingDisabledDeviceTypes)
        {
            if (Editor.DisabledDeviceTypeOptions.Count == 0) return;
            SelectedDeviceTypeCheckIndex = (SelectedDeviceTypeCheckIndex - 1 + Editor.DisabledDeviceTypeOptions.Count) % Editor.DisabledDeviceTypeOptions.Count;
            return;
        }

        if (IsBrowsingPlayerAssignments)
        {
            if (Editor.PlayerDeviceAssignmentEntries.Count == 0) return;
            SelectedPlayerAssignmentIndex = (SelectedPlayerAssignmentIndex - 1 + Editor.PlayerDeviceAssignmentEntries.Count) % Editor.PlayerDeviceAssignmentEntries.Count;
            return;
        }

        if (IsEditorOpen)
        {
            EditorFocusIndex = (EditorFocusIndex - 1 + EditorPositionCount) % EditorPositionCount;
            return;
        }

        if (FilteredGames.Count == 0) return;
        int idx = SelectedGame is null ? 0 : FilteredGames.IndexOf(SelectedGame);
        idx = (idx - 1 + FilteredGames.Count) % FilteredGames.Count;
        SelectedGame = FilteredGames[idx];
    }

    public void NavigateDown()
    {
        if (IsCategoryOptionsFocused)
        {
            if (Editor.CategoryOptions.Count == 0) return;
            SelectedCategoryOptionIndex = (SelectedCategoryOptionIndex + 1) % Editor.CategoryOptions.Count;
            return;
        }

        if (IsBrowsingBiosOverrides)
        {
            if (Editor.BiosOverridePaths.Count == 0) return;
            SelectedBiosOverrideIndex = (SelectedBiosOverrideIndex + 1) % Editor.BiosOverridePaths.Count;
            return;
        }

        if (IsBrowsingDisabledDeviceTypes)
        {
            if (Editor.DisabledDeviceTypeOptions.Count == 0) return;
            SelectedDeviceTypeCheckIndex = (SelectedDeviceTypeCheckIndex + 1) % Editor.DisabledDeviceTypeOptions.Count;
            return;
        }

        if (IsBrowsingPlayerAssignments)
        {
            if (Editor.PlayerDeviceAssignmentEntries.Count == 0) return;
            SelectedPlayerAssignmentIndex = (SelectedPlayerAssignmentIndex + 1) % Editor.PlayerDeviceAssignmentEntries.Count;
            return;
        }

        if (IsEditorOpen)
        {
            EditorFocusIndex = (EditorFocusIndex + 1) % EditorPositionCount;
            return;
        }

        if (FilteredGames.Count == 0) return;
        int idx = SelectedGame is null ? 0 : FilteredGames.IndexOf(SelectedGame);
        idx = (idx + 1) % FilteredGames.Count;
        SelectedGame = FilteredGames[idx];
    }

    public void NavigateLeft()
    {
        if (!IsEditorOpen || IsBrowsingBiosOverrides || IsBrowsingDisabledDeviceTypes || IsBrowsingPlayerAssignments) return;
        switch (EditorFocusIndex)
        {
            case 1: CycleSystem(-1); break;
            case 3: CycleEmulator(-1); break;
            case 5: Editor.Players = Math.Max(1, Editor.Players - 1); break;
            case 22: PendingAssignmentPlayerIndex = Math.Max(1, PendingAssignmentPlayerIndex - 1); break;
            case 23: CyclePendingAssignmentDevice(-1); break;
            case 28: _ = CycleScrapeResultAsync(-1); break;
        }
    }

    public void NavigateRight()
    {
        if (!IsEditorOpen || IsBrowsingBiosOverrides || IsBrowsingDisabledDeviceTypes || IsBrowsingPlayerAssignments) return;
        switch (EditorFocusIndex)
        {
            case 1: CycleSystem(1); break;
            case 3: CycleEmulator(1); break;
            case 5: Editor.Players = Math.Min(8, Editor.Players + 1); break;
            case 22: PendingAssignmentPlayerIndex = Math.Min(8, PendingAssignmentPlayerIndex + 1); break;
            case 23: CyclePendingAssignmentDevice(1); break;
            case 28: _ = CycleScrapeResultAsync(1); break;
        }
    }

    private void CyclePendingAssignmentDevice(int delta)
    {
        if (AssignableKnownDevices.Count == 0) return;
        int idx = (PendingAssignmentDeviceIndex + delta + AssignableKnownDevices.Count) % AssignableKnownDevices.Count;
        PendingAssignmentDeviceIndex = idx;
        OnPropertyChanged(nameof(PendingAssignmentDeviceLabel));
    }

    private void CycleSystem(int delta)
    {
        if (Systems.Count == 0) return;
        int idx = Systems.ToList().FindIndex(s => s.Id == Editor.SelectedSystemId);
        idx = (idx + delta + Systems.Count) % Systems.Count;
        Editor.SelectedSystemId = Systems[idx].Id;
    }

    private void CycleEmulator(int delta)
    {
        if (Emulators.Count == 0) return;
        int idx = Emulators.ToList().FindIndex(e => e.Id == Editor.SelectedEmulatorId);
        idx = (idx + delta + Emulators.Count) % Emulators.Count;
        Editor.SelectedEmulatorId = Emulators[idx].Id;
    }

    /// <summary>Back while the category checkbox sub-mode is focused exits just that,
    /// back to the flat field list — the editor itself stays open so in-progress
    /// changes (including category selections just made) aren't lost. Returns true if
    /// it handled Back, so the caller doesn't also close the whole editor.</summary>
    public bool TryExitCategoryOptions()
    {
        if (!IsCategoryOptionsFocused) return false;
        IsCategoryOptionsFocused = false;
        return true;
    }

    /// <summary>Back while browsing the BIOS override sub-list exits just that, back
    /// to the flat field list — same convention as the category checkbox grid above.</summary>
    public bool TryExitBiosOverrides()
    {
        if (!IsBrowsingBiosOverrides) return false;
        IsBrowsingBiosOverrides = false;
        return true;
    }

    /// <summary>Back while browsing the disabled-peripherals grid exits just that,
    /// back to the flat field list — same convention as every other nested
    /// list/grid in this editor.</summary>
    public bool TryExitDisabledDeviceTypes()
    {
        if (!IsBrowsingDisabledDeviceTypes) return false;
        IsBrowsingDisabledDeviceTypes = false;
        return true;
    }

    /// <summary>Back while browsing the player-device-assignments list exits just
    /// that, back to the flat field list — same convention as every other nested
    /// list/grid in this editor.</summary>
    public bool TryExitPlayerAssignments()
    {
        if (!IsBrowsingPlayerAssignments) return false;
        IsBrowsingPlayerAssignments = false;
        return true;
    }

    public async Task ConfirmAsync()
    {
        if (!IsEditorOpen)
        {
            if (SelectedGame is not null) EditSelected();
            return;
        }

        if (IsCategoryOptionsFocused)
        {
            if (SelectedCategoryOptionIndex >= 0 && SelectedCategoryOptionIndex < Editor.CategoryOptions.Count)
            {
                var opt = Editor.CategoryOptions[SelectedCategoryOptionIndex];
                opt.IsChecked = !opt.IsChecked;
            }
            return;
        }

        if (IsBrowsingBiosOverrides)
        {
            if (SelectedBiosOverrideIndex >= 0 && SelectedBiosOverrideIndex < Editor.BiosOverridePaths.Count)
            {
                Editor.BiosOverridePaths.RemoveAt(SelectedBiosOverrideIndex);
                if (Editor.BiosOverridePaths.Count == 0) IsBrowsingBiosOverrides = false;
                else SelectedBiosOverrideIndex = Math.Clamp(SelectedBiosOverrideIndex, 0, Editor.BiosOverridePaths.Count - 1);
            }
            return;
        }

        if (IsBrowsingDisabledDeviceTypes)
        {
            if (SelectedDeviceTypeCheckIndex >= 0 && SelectedDeviceTypeCheckIndex < Editor.DisabledDeviceTypeOptions.Count)
            {
                var opt = Editor.DisabledDeviceTypeOptions[SelectedDeviceTypeCheckIndex];
                opt.IsChecked = !opt.IsChecked;
            }
            return;
        }

        if (IsBrowsingPlayerAssignments)
        {
            if (SelectedPlayerAssignmentIndex >= 0 && SelectedPlayerAssignmentIndex < Editor.PlayerDeviceAssignmentEntries.Count)
            {
                Editor.PlayerDeviceAssignmentEntries.RemoveAt(SelectedPlayerAssignmentIndex);
                if (Editor.PlayerDeviceAssignmentEntries.Count == 0) IsBrowsingPlayerAssignments = false;
                else SelectedPlayerAssignmentIndex = Math.Clamp(SelectedPlayerAssignmentIndex, 0, Editor.PlayerDeviceAssignmentEntries.Count - 1);
            }
            return;
        }

        switch (EditorFocusIndex)
        {
            case 0: _virtualKeyboard.Open("Title", Editor.Title, v => Editor.Title = v); break;
            case 2:
                if (Editor.CategoryOptions.Count > 0)
                {
                    IsCategoryOptionsFocused = true;
                    SelectedCategoryOptionIndex = Math.Clamp(SelectedCategoryOptionIndex, 0, Editor.CategoryOptions.Count - 1);
                }
                break;
            case 4: _virtualKeyboard.Open("Genre", Editor.Genre, v => Editor.Genre = v); break;
            case 7: _virtualKeyboard.Open("ROM Path", Editor.RomPath, v => Editor.RomPath = v); break;
            case 8: _virtualKeyboard.Open("Process Name Override", Editor.ProcessNameOverride, v => Editor.ProcessNameOverride = v); break;
            case 9: _virtualKeyboard.Open("Cover Path", Editor.CoverPath, v => Editor.CoverPath = v); break;
            case 10: _virtualKeyboard.Open("Background Path", Editor.BackgroundPath, v => Editor.BackgroundPath = v); break;
            case 11: _virtualKeyboard.Open("Logo Path", Editor.LogoPath, v => Editor.LogoPath = v); break;
            case 12: _virtualKeyboard.Open("Video Path", Editor.VideoPath, v => Editor.VideoPath = v); break;
            case 13: _virtualKeyboard.Open("Marquee Path", Editor.MarqueePath, v => Editor.MarqueePath = v); break;
            case 14: await BrowseBezelOverrideAsync(); break;
            case 15: OpenNumericKeyboard("Display Width Override (0 = use system default)", Editor.DisplayModeOverrideWidth, v => Editor.DisplayModeOverrideWidth = v); break;
            case 16: OpenNumericKeyboard("Display Height Override (0 = use system default)", Editor.DisplayModeOverrideHeight, v => Editor.DisplayModeOverrideHeight = v); break;
            case 17: OpenNumericKeyboard("Display Refresh Hz Override (0 = use system default)", Editor.DisplayModeOverrideRefreshHz, v => Editor.DisplayModeOverrideRefreshHz = v); break;
            case 18:
                if (Editor.BiosOverridePaths.Count > 0)
                {
                    IsBrowsingBiosOverrides = true;
                    SelectedBiosOverrideIndex = Math.Clamp(SelectedBiosOverrideIndex, 0, Editor.BiosOverridePaths.Count - 1);
                }
                break;
            case 19: await BrowseAddBiosOverrideAsync(); break;
            case 20:
                if (Editor.DisabledDeviceTypeOptions.Count > 0)
                {
                    IsBrowsingDisabledDeviceTypes = true;
                    SelectedDeviceTypeCheckIndex = Math.Clamp(SelectedDeviceTypeCheckIndex, 0, Editor.DisabledDeviceTypeOptions.Count - 1);
                }
                break;
            case 21: _virtualKeyboard.Open("DemulShooter Target", Editor.DemulShooterTarget, v => Editor.DemulShooterTarget = v); break;
            case 24: AddPendingPlayerDeviceAssignment(); break;
            case 25:
                if (Editor.PlayerDeviceAssignmentEntries.Count > 0)
                {
                    IsBrowsingPlayerAssignments = true;
                    SelectedPlayerAssignmentIndex = Math.Clamp(SelectedPlayerAssignmentIndex, 0, Editor.PlayerDeviceAssignmentEntries.Count - 1);
                }
                break;
            case 26: await SaveEditorAsync(); break;
            case 27: CancelEditor(); break;
            case 6: Editor.IsFavorite = !Editor.IsFavorite; break;
            case 28: await ScrapeAsync(); break;
            case 29: await GenerateCardAsync(); break;
            // 1, 3, 5 (System/Emulator/Players), 22, 23 (pending assignment player
            // index/device) are all adjusted via Left/Right, not Confirm.
        }
    }

    [RelayCommand]
    private void AddPendingPlayerDeviceAssignment()
    {
        if (AssignableKnownDevices.Count == 0) return;
        var device = AssignableKnownDevices[Math.Clamp(PendingAssignmentDeviceIndex, 0, AssignableKnownDevices.Count - 1)];

        // Adding the same device to the same player slot twice would just duplicate
        // it in the ranked list for no benefit — quietly no-op instead.
        if (Editor.PlayerDeviceAssignmentEntries.Any(e =>
                e.PlayerIndex == PendingAssignmentPlayerIndex &&
                string.Equals(e.HardwarePath, device.HardwarePath, StringComparison.OrdinalIgnoreCase)))
            return;

        Editor.PlayerDeviceAssignmentEntries.Add(new PlayerDeviceAssignmentEntry(
            PendingAssignmentPlayerIndex, device.HardwarePath, device.FriendlyName));
    }

    // ── Scraper / ComfyUI ─────────────────────────────────────────────────────
    // Used only for downloading a scraped/generated image to a local temp file —
    // separate from the scraper sources' own typed HttpClients (UGL.Scraping),
    // which handle the actual API calls.
    private static readonly HttpClient _imageDownloadClient = new();

    // Scratch location for downloaded-but-not-yet-saved images. Deliberately inside
    // the app's own media\ folder (not the OS temp directory) — this app is meant to
    // stay portable, and Path.GetTempPath() always resolves to the *system* temp
    // folder (C:\Users\...\AppData\Local\Temp) regardless of which drive UGL itself
    // runs from, which both breaks portability and leaves orphaned files behind
    // forever since nothing ever cleaned that location up. CopyMediaFileAsync
    // deletes the source file from here once it's been copied to its real
    // destination on Save (see there), so this folder stays essentially empty in
    // normal use rather than accumulating downloads indefinitely.
    private static string ScrapeScratchDir => Path.Combine(AppContext.BaseDirectory, "media", "_scrape_temp");

    // Search results from the most recent Scrape, kept so Left/Right on the Scrape
    // field can step through alternate matches instead of being stuck with whatever
    // the first (possibly wrong-platform/wrong-game) result was — see CycleScrapeResultAsync.
    private List<ScraperSearchResult> _lastScrapeResults = [];
    private int _scrapeResultIndex;
    private ScraperSourceType _lastScrapeSource;

    /// <summary>
    /// Searches the preferred scraper source by the editor's current Title, narrowed
    /// to the selected System's name so a title shared across multiple platforms
    /// (e.g. a game ported between systems) is much less likely to match the wrong
    /// version — and applies the first result. If that's still the wrong match,
    /// Left/Right while this field is focused steps through the other results
    /// (see CycleScrapeResultAsync) rather than requiring a re-search.
    /// </summary>
    [RelayCommand]
    private async Task ScrapeAsync()
    {
        if (string.IsNullOrWhiteSpace(Editor.Title))
        {
            StatusMessage = "Enter a Title before scraping.";
            return;
        }

        var settings = await _scraperSettingsRepo.GetSettingsAsync();
        var source = settings.PreferredSource;
        var systemName = Systems.FirstOrDefault(s => s.Id == Editor.SelectedSystemId)?.Name;

        StatusMessage = $"Searching {source}…";
        var results = await _scraperService.SearchAsync(source, Editor.Title, systemName);
        _lastScrapeResults = results.ToList();
        _lastScrapeSource = source;
        _scrapeResultIndex = 0;

        if (_lastScrapeResults.Count == 0)
        {
            StatusMessage = $"No {source} results for '{Editor.Title}'.";
            return;
        }

        await ApplyScrapeResultAsync();
    }

    /// <summary>Left/Right on the Scrape field — steps through the other matches from
    /// the most recent search without re-querying the source.</summary>
    public async Task CycleScrapeResultAsync(int delta)
    {
        if (_lastScrapeResults.Count == 0) return;
        _scrapeResultIndex = (_scrapeResultIndex + delta + _lastScrapeResults.Count) % _lastScrapeResults.Count;
        await ApplyScrapeResultAsync();
    }

    private async Task ApplyScrapeResultAsync()
    {
        var picked = _lastScrapeResults[_scrapeResultIndex];
        var source = _lastScrapeSource;

        StatusMessage = $"Match {_scrapeResultIndex + 1}/{_lastScrapeResults.Count}: fetching '{picked.Title}'…";
        var details = await _scraperService.GetDetailsAsync(source, picked.SourceGameId);
        if (details is null)
        {
            StatusMessage = $"Match {_scrapeResultIndex + 1}/{_lastScrapeResults.Count}: '{picked.Title}' on {source} but couldn't fetch details.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(details.Genre)) Editor.Genre = details.Genre;
        if (details.Players is { } p) Editor.Players = Math.Clamp(p, 1, 8);

        var coverPath = await DownloadToScratchAsync(details.CoverImageUrl, "cover");
        if (coverPath is not null) Editor.CoverPath = coverPath;

        var logoPath = await DownloadToScratchAsync(details.LogoImageUrl, "logo");
        if (logoPath is not null) Editor.LogoPath = logoPath;

        var marqueePath = await DownloadToScratchAsync(details.MarqueeImageUrl, "marquee");
        if (marqueePath is not null) Editor.MarqueePath = marqueePath;

        var platformSuffix = string.IsNullOrWhiteSpace(picked.Platform) ? "" : $" [{picked.Platform}]";
        StatusMessage = $"Match {_scrapeResultIndex + 1}/{_lastScrapeResults.Count}: '{details.Title}'{platformSuffix} on {source} — Left/Right for other matches, review, then Save.";
        _logger.LogInformation("Scraped '{Title}' from {Source} (matched '{MatchedTitle}', {Index}/{Count}).",
            Editor.Title, source, details.Title, _scrapeResultIndex + 1, _lastScrapeResults.Count);
    }

    /// <summary>
    /// Generates card art via the configured ComfyUI workflow, using the game's
    /// Title/Genre as the prompt, and applies it as the Cover Art field (like
    /// ScrapeAsync, the actual file copy happens on Save — this just points
    /// Editor.CoverPath at a downloaded scratch file).
    /// </summary>
    [RelayCommand]
    private async Task GenerateCardAsync()
    {
        if (string.IsNullOrWhiteSpace(Editor.Title))
        {
            StatusMessage = "Enter a Title before generating card art.";
            return;
        }

        var prompt = string.IsNullOrWhiteSpace(Editor.Genre)
            ? $"video game box art for \"{Editor.Title}\""
            : $"video game box art for \"{Editor.Title}\", a {Editor.Genre} game";

        StatusMessage = "Generating card art via ComfyUI… this can take a while.";
        var imageBytes = await _comfyUiClient.GenerateImageAsync(prompt);
        if (imageBytes is null)
        {
            StatusMessage = "ComfyUI generation failed — check logs\\ugl.log for the actual error (endpoint unreachable, workflow rejected, no {{PROMPT}} token, or a timed-out render).";
            return;
        }

        try
        {
            Directory.CreateDirectory(ScrapeScratchDir);
            var scratchPath = Path.Combine(ScrapeScratchDir, $"comfyui_{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(scratchPath, imageBytes);
            Editor.CoverPath = scratchPath;
            StatusMessage = "Card art generated. Review it, then Save.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save ComfyUI-generated image to the scratch folder.");
            StatusMessage = "Generated the image but failed to save it locally — see logs.";
        }
    }

    private async Task<string?> DownloadToScratchAsync(string? url, string label)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var bytes = await _imageDownloadClient.GetByteArrayAsync(url);
            var ext = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            Directory.CreateDirectory(ScrapeScratchDir);
            var scratchPath = Path.Combine(ScrapeScratchDir, $"{label}_{Guid.NewGuid():N}{ext}");
            await File.WriteAllBytesAsync(scratchPath, bytes);
            return scratchPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download scraped {Label} image from {Url}.", label, url);
            return null;
        }
    }

    private void ApplySearch()
    {
        FilteredGames.Clear();
        var term = SearchText.Trim();
        var matches = string.IsNullOrWhiteSpace(term)
            ? AllGames
            : AllGames.Where(g => g.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                  g.Genre.Contains(term, StringComparison.OrdinalIgnoreCase));
        foreach (var g in matches) FilteredGames.Add(g);
    }

    // ── Commands ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddNew()
    {
        Editor.PopulateFrom(new Game
        {
            Id = string.Empty, Title = string.Empty, SystemId = string.Empty,
            CategoryIds = [], RomPath = string.Empty, EmulatorId = string.Empty
        });
        Editor.SyncPlayerDeviceAssignments([], _peripheralRegistry.KnownDevices);
        Editor.Id = string.Empty; // Force new-game Id generation on save
        IsEditorOpen = true;
        EditorFocusIndex = 0;
        IsCategoryOptionsFocused = false;
        SelectedCategoryOptionIndex = 0;
        IsBrowsingBiosOverrides = false;
        SelectedBiosOverrideIndex = 0;
        IsBrowsingDisabledDeviceTypes = false;
        SelectedDeviceTypeCheckIndex = 0;
        IsBrowsingPlayerAssignments = false;
        SelectedPlayerAssignmentIndex = 0;
        PendingAssignmentPlayerIndex = 1;
        PendingAssignmentDeviceIndex = 0;
        SelectedGame = null;
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedGame is null) return;
        Editor.PopulateFrom(SelectedGame);
        Editor.SyncPlayerDeviceAssignments(SelectedGame.PlayerDeviceAssignments, _peripheralRegistry.KnownDevices);
        IsEditorOpen = true;
        EditorFocusIndex = 0;
        IsCategoryOptionsFocused = false;
        SelectedCategoryOptionIndex = 0;
        IsBrowsingBiosOverrides = false;
        SelectedBiosOverrideIndex = 0;
        IsBrowsingDisabledDeviceTypes = false;
        SelectedDeviceTypeCheckIndex = 0;
        IsBrowsingPlayerAssignments = false;
        SelectedPlayerAssignmentIndex = 0;
        PendingAssignmentPlayerIndex = 1;
        PendingAssignmentDeviceIndex = 0;
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedGame is null) return;
        await _gameRepo.DeleteAsync(SelectedGame.Id);
        await _gameRepo.SaveAsync();
        AllGames.Remove(SelectedGame);
        ApplySearch();
        SelectedGame = null;
        StatusMessage = "Game deleted.";
        GameCatalogChanged?.Invoke();
    }

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        Editor.Validate();
        if (!Editor.IsValid)
        {
            StatusMessage = Editor.ValidationError;
            return;
        }

        var game = Editor.ToGame();

        // The editor form has no LastPlayed field (it's runtime-tracked, set only when
        // a game is actually launched — see MainWindowViewModel.OnGameConfirmed), so
        // ToGame() never populates it. Preserve whatever the existing stored entry had
        // rather than silently resetting it to null on every metadata edit.
        var existing = AllGames.FirstOrDefault(g => g.Id == game.Id);
        if (existing is not null) game.LastPlayed = existing.LastPlayed;

        // Copy media files into media/ folder
        game = await CopyMediaFilesAsync(game);

        await _gameRepo.AddOrUpdateAsync(game);
        await _gameRepo.SaveAsync();

        // Refresh list
        var idx = AllGames.IndexOf(AllGames.FirstOrDefault(g => g.Id == game.Id)!);
        if (idx >= 0) AllGames[idx] = game;
        else AllGames.Add(game);

        ApplySearch();
        IsEditorOpen = false;
        StatusMessage = $"'{game.Title}' saved.";
        GameCatalogChanged?.Invoke();
        _logger.LogInformation("Game saved: {Title}", game.Title);
    }

    [RelayCommand]
    private void CancelEditor()
    {
        IsEditorOpen = false;
        StatusMessage = string.Empty;
    }

    // ── Media browse commands ──────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseCoverAsync()
        => Editor.CoverPath = await BrowseImageAsync() ?? Editor.CoverPath;

    [RelayCommand]
    private async Task BrowseBackgroundAsync()
        => Editor.BackgroundPath = await BrowseImageAsync() ?? Editor.BackgroundPath;

    [RelayCommand]
    private async Task BrowseLogoAsync()
        => Editor.LogoPath = await BrowseImageAsync() ?? Editor.LogoPath;

    [RelayCommand]
    private async Task BrowseVideoAsync()
        => Editor.VideoPath = await BrowseVideoFileAsync() ?? Editor.VideoPath;

    [RelayCommand]
    private async Task BrowseMarqueeAsync()
        => Editor.MarqueePath = await BrowseImageAsync() ?? Editor.MarqueePath;

    [RelayCommand]
    private async Task BrowseBezelOverrideAsync()
        => Editor.BezelOverridePath = await BrowseImageAsync() ?? Editor.BezelOverridePath;

    /// <summary>Opens the virtual keyboard for a numeric field, parsing the committed
    /// text back to an int (0 on anything unparseable, matching "not set").</summary>
    private void OpenNumericKeyboard(string label, int currentValue, Action<int> commit)
    {
        _virtualKeyboard.Open(label, currentValue == 0 ? string.Empty : currentValue.ToString(),
            v => commit(int.TryParse(v, out var n) ? n : 0));
    }

    [RelayCommand]
    private async Task BrowseAddBiosOverrideAsync()
    {
        var path = await BrowseFileAsync("BIOS Files", ["*.bin", "*.rom", "*.img", "*.*"]);
        if (path is not null) Editor.BiosOverridePaths.Add(path);
    }

    [RelayCommand]
    private void RemoveBiosOverride(string path)
    {
        var idx = Editor.BiosOverridePaths.IndexOf(path);
        Editor.BiosOverridePaths.Remove(path);
        if (Editor.BiosOverridePaths.Count > 0)
            SelectedBiosOverrideIndex = Math.Clamp(idx, 0, Editor.BiosOverridePaths.Count - 1);
        else
            IsBrowsingBiosOverrides = false;
    }

    [RelayCommand]
    private async Task BrowseRomAsync()
        => Editor.RomPath = await BrowseFileAsync("ROM Files", ["*.zip", "*.bin", "*.iso", "*.nes", "*.sfc", "*.z64", "*.rom", "*.*"]) ?? Editor.RomPath;

    private Task<string?> BrowseImageAsync()
        => BrowseFileAsync("Image Files", ["*.jpg", "*.jpeg", "*.png", "*.webp"]);

    private Task<string?> BrowseVideoFileAsync()
        => BrowseFileAsync("Video Files", ["*.mp4", "*.mkv", "*.avi"]);

    private async Task<string?> BrowseFileAsync(string title, string[] patterns)
    {
        if (BrowseFileRequested is null) return null;
        return await BrowseFileRequested.Invoke(title, patterns);
    }

    // ── Drag and drop ──────────────────────────────────────────────────────

    /// <summary>
    /// Called by the View when files are dropped onto a media field.
    /// fieldName: "cover", "background", "logo", "video", "marquee"
    /// </summary>
    public void AcceptMediaDrop(string fieldName, string filePath)
    {
        switch (fieldName.ToLowerInvariant())
        {
            case "cover":      Editor.CoverPath      = filePath; break;
            case "background": Editor.BackgroundPath  = filePath; break;
            case "logo":       Editor.LogoPath        = filePath; break;
            case "video":      Editor.VideoPath       = filePath; break;
            case "marquee":    Editor.MarqueePath     = filePath; break;
        }
    }

    // ── Media copy ─────────────────────────────────────────────────────────

    /// <summary>
    /// Copies any new media files into the correct media/ subfolder,
    /// renames them to {systemId}-{gameId}.{ext}, and returns a new
    /// Game with updated Media paths pointing to the copied locations.
    /// </summary>
    private async Task<Game> CopyMediaFilesAsync(Game game)
    {
        var mediaRoot = Path.Combine(AppContext.BaseDirectory,
            _config.Settings.MediaRootPath);

        var cover      = await CopyMediaFileAsync(game, game.Media.CoverPath,      "covers",      mediaRoot);
        var background = await CopyMediaFileAsync(game, game.Media.BackgroundPath, "backgrounds", mediaRoot);
        var logo       = await CopyMediaFileAsync(game, game.Media.LogoPath,       "logos",       mediaRoot);
        var video      = await CopyMediaFileAsync(game, game.Media.VideoPath,      "video",       mediaRoot);
        var marquee    = await CopyMediaFileAsync(game, game.Media.MarqueePath,    "marquees",    mediaRoot);

        return new Game
        {
            Id = game.Id, Title = game.Title, SystemId = game.SystemId,
            CategoryIds = game.CategoryIds, RomPath = game.RomPath,
            ProcessNameOverride = game.ProcessNameOverride,
            EmulatorId = game.EmulatorId, Genre = game.Genre,
            Players = game.Players, IsFavorite = game.IsFavorite,
            LastPlayed = game.LastPlayed,
            BezelOverridePath = game.BezelOverridePath,
            DisplayModeOverride = game.DisplayModeOverride,
            DemulShooterTarget = game.DemulShooterTarget,
            PlayerDeviceAssignments = game.PlayerDeviceAssignments,
            BiosOverridePaths = game.BiosOverridePaths,
            DisabledDeviceTypes = game.DisabledDeviceTypes,
            Media = new GameMedia
            {
                CoverPath      = cover      ?? game.Media.CoverPath,
                BackgroundPath = background ?? game.Media.BackgroundPath,
                LogoPath       = logo       ?? game.Media.LogoPath,
                VideoPath      = video      ?? game.Media.VideoPath,
                MarqueePath    = marquee    ?? game.Media.MarqueePath,
            }
        };
    }

    private async Task<string?> CopyMediaFileAsync(
        Game game, string sourcePath, string subfolder, string mediaRoot)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        // sourcePath comes from GameEditViewModel.ToGame(), which now stores these
        // relative to the app's own folder when possible — resolve to an absolute
        // path first, before any file operations, so this doesn't silently depend on
        // the process's current working directory happening to match the exe's own.
        var absoluteSource = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(sourcePath);

        // If the path is already inside media/, no copy needed.
        var normalSource = Path.GetFullPath(absoluteSource);
        var normalMedia  = Path.GetFullPath(mediaRoot);
        if (normalSource.StartsWith(normalMedia, StringComparison.OrdinalIgnoreCase))
            return UGL.Core.Utilities.PortablePathHelper.ToPortablePath(normalSource);

        if (!File.Exists(absoluteSource))
        {
            _logger.LogWarning("Media source not found, skipping copy: {Path}", absoluteSource);
            return null;
        }

        var ext = Path.GetExtension(absoluteSource).ToLowerInvariant();
        var slug = $"{game.SystemId}-{game.Id}".ToLowerInvariant();
        var destDir = Path.Combine(mediaRoot, subfolder);
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, slug + ext);

        await Task.Run(() => File.Copy(absoluteSource, destPath, overwrite: true));

        // Evict cached version so the launcher picks up the new file.
        _cache.EvictImage(destPath);

        _logger.LogInformation("Media copied: {Source} → {Dest}", absoluteSource, destPath);

        // A scraped/ComfyUI-generated source lives in the scratch folder purely
        // pending this copy — delete it now so the scratch folder doesn't
        // accumulate every download ever made, portable or not.
        if (normalSource.StartsWith(Path.GetFullPath(ScrapeScratchDir), StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(absoluteSource); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not delete scratch file {Path} after copy (non-fatal).", absoluteSource); }
        }

        // Stored relative to the app's own folder when possible, so this keeps
        // working if the whole portable install moves to a different drive letter
        // or machine — MediaAssetResolver.ResolvePath already resolves either form
        // back to an absolute path when loading.
        return UGL.Core.Utilities.PortablePathHelper.ToPortablePath(destPath);
    }
}
