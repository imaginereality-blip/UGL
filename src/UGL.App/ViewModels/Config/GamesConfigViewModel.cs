using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
    private readonly ConfirmDialogViewModel _confirmDialog;
    private readonly ILogger<GamesConfigViewModel> _logger;

    // ── Editor sub-tabs: Settings / Art ─────────────────────────────────────
    // Mirrors AudioConfigViewModel's Music/Sounds split: two independent focus-index
    // cycles, switched via LB/RB (SwitchSubTabLeft/Right, wired from
    // ConfigEditorViewModel) rather than folded into one long flat list. Room to add
    // further sub-tabs later (e.g. per-game saves/play-data) the same way.
    [ObservableProperty] private bool _isSettingsTabActive = true;
    [ObservableProperty] private bool _isArtTabActive;

    [RelayCommand]
    private void SelectSettingsTab()
    {
        IsSettingsTabActive = true;
        IsArtTabActive = false;
    }

    [RelayCommand]
    private void SelectArtTab()
    {
        IsSettingsTabActive = false;
        IsArtTabActive = true;
    }

    public void SwitchSubTabLeft() => SelectSettingsTab();
    public void SwitchSubTabRight() => SelectArtTab();

    /// <summary>
    /// Field position within the Settings sub-tab — 0=Title, 4=Genre, 7-8=ROM/process-
    /// name (text, open the virtual keyboard), 21=Save, 22=Cancel. System(1)/
    /// Emulator(3) cycle via Left/Right, Players(5) adjusts via Left/Right,
    /// Favorite(6) toggles via Confirm. Category(2) is a multi-select checkbox grid —
    /// Confirm enters a sub-mode (see IsCategoryOptionsFocused below), matching the
    /// same "enter list, Back exits it" convention as Audio's track sub-list.
    /// </summary>
    [ObservableProperty] private int _settingsFocusIndex;
    private const int SettingsPositionCount = 23;

    /// <summary>
    /// Field position within the Art sub-tab — 0-5 are media path fields (text, open
    /// the virtual keyboard), 6 the "use as cover" checkbox, 7 Scrape, 8 Resize Cover
    /// to Card, 9 Generate Poster Collage (ComfyUI), 10-11 Save/Cancel (same commands
    /// as the Settings sub-tab's — either sub-tab can commit or discard the whole
    /// game, not just its own fields).
    /// </summary>
    [ObservableProperty] private int _artFocusIndex;
    private const int ArtPositionCount = 12;

    // ── Category checkbox sub-mode — entered via Confirm at SettingsFocusIndex==2 ─────
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

    // ── BIOS override sub-mode — entered via Confirm at SettingsFocusIndex==13 ────────
    // Nearly always empty; only needed for the rare game that requires a BIOS
    // different from its emulator's own default list (Emulator.BiosPaths). Plain
    // strings, not a wrapper VM — the ListBox's own SelectedIndex binding drives the
    // highlight directly, same pattern as Audio's track list.
    [ObservableProperty] private bool _isBrowsingBiosOverrides;
    [ObservableProperty] private int _selectedBiosOverrideIndex;

    // ── Disabled peripherals sub-mode — entered via Confirm at SettingsFocusIndex==15 ─
    // Nearly always all unchecked; only needed for the rare game where a lightgun,
    // wheel, etc. would interfere (e.g. disabling both for a fighting game).
    [ObservableProperty] private bool _isBrowsingDisabledDeviceTypes;
    [ObservableProperty] private int _selectedDeviceTypeCheckIndex;

    // ── Player device assignments — richer, per-slot alternative to disabled
    // peripherals above (Game.PlayerDeviceAssignments). Two flat "pending" fields
    // (cycled via Left/Right, same convention as System/Emulator/Players) build up
    // one entry at a time; AddAssignment commits it; the list sub-mode (entered via
    // Confirm at SettingsFocusIndex==20) removes existing ones. Nearly always empty.
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

    // ── Settings sub-tab field highlight (drives the same Classes-binding highlight
    // trick used everywhere else — sidebar menu, Peripheral Hooks, keyboard keys) ────
    public bool IsTitleFocused          => SettingsFocusIndex == 0;
    public bool IsSystemFocused         => SettingsFocusIndex == 1;
    public bool IsCategoryFocused       => SettingsFocusIndex == 2;
    public bool IsEmulatorFocused       => SettingsFocusIndex == 3;
    public bool IsGenreFocused          => SettingsFocusIndex == 4;
    public bool IsPlayersFocused        => SettingsFocusIndex == 5;
    public bool IsFavoriteFocused       => SettingsFocusIndex == 6;
    public bool IsRomPathFocused        => SettingsFocusIndex == 7;
    public bool IsProcessNameOverrideFocused => SettingsFocusIndex == 8;
    public bool IsBezelOverrideFocused    => SettingsFocusIndex == 9;
    public bool IsDisplayWidthOverrideFocused     => SettingsFocusIndex == 10;
    public bool IsDisplayHeightOverrideFocused    => SettingsFocusIndex == 11;
    public bool IsDisplayRefreshHzOverrideFocused => SettingsFocusIndex == 12;
    public bool IsBiosOverrideListFocused => SettingsFocusIndex == 13;
    public bool IsAddBiosOverrideFocused  => SettingsFocusIndex == 14;
    public bool IsDisabledDeviceTypesFocused => SettingsFocusIndex == 15;
    public bool IsDemulShooterTargetFocused => SettingsFocusIndex == 16;
    public bool IsPendingAssignmentPlayerIndexFocused => SettingsFocusIndex == 17;
    public bool IsPendingAssignmentDeviceFocused      => SettingsFocusIndex == 18;
    public bool IsAddAssignmentFocused                => SettingsFocusIndex == 19;
    public bool IsPlayerDeviceAssignmentsFocused => SettingsFocusIndex == 20;
    public bool IsSaveFocused           => SettingsFocusIndex == 21;
    public bool IsCancelFocused         => SettingsFocusIndex == 22;

    partial void OnSettingsFocusIndexChanged(int value)
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
    }

    // ── Art sub-tab field highlight ──────────────────────────────────────────
    public bool IsCoverPathFocused      => ArtFocusIndex == 0;
    public bool IsBackgroundPathFocused => ArtFocusIndex == 1;
    public bool IsLogoPathFocused       => ArtFocusIndex == 2;
    public bool IsVideoPathFocused      => ArtFocusIndex == 3;
    public bool IsMarqueePathFocused    => ArtFocusIndex == 4;
    public bool IsCardArtPathFocused    => ArtFocusIndex == 5;
    public bool IsPreferCardArtAsCoverFocused => ArtFocusIndex == 6;
    public bool IsScrapeFocused         => ArtFocusIndex == 7;
    public bool IsResizeCoverToCardFocused => ArtFocusIndex == 8;
    public bool IsGenerateCardFocused   => ArtFocusIndex == 9;
    public bool IsArtSaveFocused        => ArtFocusIndex == 10;
    public bool IsArtCancelFocused      => ArtFocusIndex == 11;

    partial void OnArtFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCoverPathFocused));
        OnPropertyChanged(nameof(IsBackgroundPathFocused));
        OnPropertyChanged(nameof(IsLogoPathFocused));
        OnPropertyChanged(nameof(IsVideoPathFocused));
        OnPropertyChanged(nameof(IsMarqueePathFocused));
        OnPropertyChanged(nameof(IsCardArtPathFocused));
        OnPropertyChanged(nameof(IsPreferCardArtAsCoverFocused));
        OnPropertyChanged(nameof(IsScrapeFocused));
        OnPropertyChanged(nameof(IsResizeCoverToCardFocused));
        OnPropertyChanged(nameof(IsGenerateCardFocused));
        OnPropertyChanged(nameof(IsArtSaveFocused));
        OnPropertyChanged(nameof(IsArtCancelFocused));
        OnPropertyChanged(nameof(FocusedArtPreview));
        OnPropertyChanged(nameof(FocusedArtPreviewLabel));
    }

    // ── Game list ──────────────────────────────────────────────────────────

    public ObservableCollection<Game> AllGames { get; } = [];
    public ObservableCollection<Game> FilteredGames { get; } = [];

    [ObservableProperty] private Game? _selectedGame;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Which of the Add/Edit/Delete buttons is gamepad-focused while the
    /// editor is closed — cycled via Left/Right (otherwise unused while just browsing
    /// the list). Defaults to Edit (1) so Confirm's existing "edit the highlighted
    /// game" behavior is unchanged unless the user explicitly cycles to Add/Delete.</summary>
    [ObservableProperty] private int _selectedListActionIndex = 1;
    public bool IsAddActionFocused    => SelectedListActionIndex == 0;
    public bool IsEditActionFocused   => SelectedListActionIndex == 1;
    public bool IsDeleteActionFocused => SelectedListActionIndex == 2;

    partial void OnSelectedListActionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAddActionFocused));
        OnPropertyChanged(nameof(IsEditActionFocused));
        OnPropertyChanged(nameof(IsDeleteActionFocused));
    }

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
        ConfirmDialogViewModel confirmDialog,
        ILogger<GamesConfigViewModel> logger)
    {
        _confirmDialog = confirmDialog;
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
            else if (args.PropertyName is nameof(GameEditViewModel.CoverPreview) or
                     nameof(GameEditViewModel.BackgroundPreview) or nameof(GameEditViewModel.LogoPreview) or
                     nameof(GameEditViewModel.MarqueePreview) or nameof(GameEditViewModel.CardArtPreview))
                OnPropertyChanged(nameof(FocusedArtPreview));
        };
    }

    /// <summary>The single large preview shown in the Art sub-tab — whichever image
    /// field currently has gamepad/keyboard focus (falls back to null for fields with
    /// no image, e.g. Video or the checkbox/buttons). One shared panel instead of a
    /// small inline thumbnail per field.</summary>
    public Avalonia.Media.Imaging.Bitmap? FocusedArtPreview => ArtFocusIndex switch
    {
        0 => Editor.CoverPreview,
        1 => Editor.BackgroundPreview,
        2 => Editor.LogoPreview,
        4 => Editor.MarqueePreview,
        5 => Editor.CardArtPreview,
        // Scrape/Resize/Generate are buttons, not fields, but showing the most
        // relevant image while they're focused makes each action's result visible
        // immediately — cycling scrape matches (Left/Right) updates the cover shown
        // here without needing to move focus to the Cover field itself.
        7 => Editor.CoverPreview,
        8 => Editor.CardArtPreview,
        9 => Editor.CardArtPreview,
        _ => null,
    };

    public string FocusedArtPreviewLabel => ArtFocusIndex switch
    {
        0 => "Cover Art",
        1 => "Background",
        2 => "Logo",
        3 => "Video Preview (no image preview)",
        4 => "Marquee",
        5 => "Card Art",
        7 => "Cover Art (from scrape — Left/Right to cycle matches)",
        8 => "Card Art (resized from Cover)",
        9 => "Card Art (generated)",
        _ => "No preview for this field",
    };

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
    // Browses the (search-filtered) game list when the editor is closed. Once the
    // editor is open, Up/Down instead moves SettingsFocusIndex or ArtFocusIndex
    // between fields (whichever sub-tab is active — see IsArtTabActive); Confirm on a
    // text field opens the virtual keyboard, on Save/Cancel triggers those actions
    // directly. LB/RB (SwitchSubTabLeft/Right) switches between the two sub-tabs.
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
            if (IsArtTabActive)
                ArtFocusIndex = (ArtFocusIndex - 1 + ArtPositionCount) % ArtPositionCount;
            else
                SettingsFocusIndex = (SettingsFocusIndex - 1 + SettingsPositionCount) % SettingsPositionCount;
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
            if (IsArtTabActive)
                ArtFocusIndex = (ArtFocusIndex + 1) % ArtPositionCount;
            else
                SettingsFocusIndex = (SettingsFocusIndex + 1) % SettingsPositionCount;
            return;
        }

        if (FilteredGames.Count == 0) return;
        int idx = SelectedGame is null ? 0 : FilteredGames.IndexOf(SelectedGame);
        idx = (idx + 1) % FilteredGames.Count;
        SelectedGame = FilteredGames[idx];
    }

    public void NavigateLeft()
    {
        if (!IsEditorOpen)
        {
            SelectedListActionIndex = (SelectedListActionIndex - 1 + 3) % 3;
            return;
        }
        if (IsBrowsingBiosOverrides || IsBrowsingDisabledDeviceTypes || IsBrowsingPlayerAssignments) return;

        if (IsArtTabActive)
        {
            if (ArtFocusIndex == 7) _ = CycleScrapeResultAsync(-1);
            return;
        }

        switch (SettingsFocusIndex)
        {
            case 1: CycleSystem(-1); break;
            case 3: CycleEmulator(-1); break;
            case 5: Editor.Players = Math.Max(1, Editor.Players - 1); break;
            case 17: PendingAssignmentPlayerIndex = Math.Max(1, PendingAssignmentPlayerIndex - 1); break;
            case 18: CyclePendingAssignmentDevice(-1); break;
        }
    }

    public void NavigateRight()
    {
        if (!IsEditorOpen)
        {
            SelectedListActionIndex = (SelectedListActionIndex + 1) % 3;
            return;
        }
        if (IsBrowsingBiosOverrides || IsBrowsingDisabledDeviceTypes || IsBrowsingPlayerAssignments) return;

        if (IsArtTabActive)
        {
            if (ArtFocusIndex == 7) _ = CycleScrapeResultAsync(1);
            return;
        }

        switch (SettingsFocusIndex)
        {
            case 1: CycleSystem(1); break;
            case 3: CycleEmulator(1); break;
            case 5: Editor.Players = Math.Min(8, Editor.Players + 1); break;
            case 17: PendingAssignmentPlayerIndex = Math.Min(8, PendingAssignmentPlayerIndex + 1); break;
            case 18: CyclePendingAssignmentDevice(1); break;
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
            switch (SelectedListActionIndex)
            {
                case 0: AddNew(); break;
                case 1: if (SelectedGame is not null) EditSelected(); break;
                case 2: if (SelectedGame is not null) await DeleteSelectedAsync(); break;
            }
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

        if (IsArtTabActive)
        {
            switch (ArtFocusIndex)
            {
                case 0: _virtualKeyboard.Open("Cover Path", Editor.CoverPath, v => Editor.CoverPath = v); break;
                case 1: _virtualKeyboard.Open("Background Path", Editor.BackgroundPath, v => Editor.BackgroundPath = v); break;
                case 2: _virtualKeyboard.Open("Logo Path", Editor.LogoPath, v => Editor.LogoPath = v); break;
                case 3: _virtualKeyboard.Open("Video Path", Editor.VideoPath, v => Editor.VideoPath = v); break;
                case 4: _virtualKeyboard.Open("Marquee Path", Editor.MarqueePath, v => Editor.MarqueePath = v); break;
                case 5: _virtualKeyboard.Open("Card Art Path", Editor.CardArtPath, v => Editor.CardArtPath = v); break;
                case 6: Editor.PreferCardArtAsCover = !Editor.PreferCardArtAsCover; break;
                case 7: await ScrapeAsync(); break;
                case 8: await ResizeCoverToCardAsync(); break;
                case 9: await GenerateCardAsync(); break;
                case 10: await SaveEditorAsync(); break;
                case 11: CancelEditor(); break;
            }
            return;
        }

        switch (SettingsFocusIndex)
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
            case 6: Editor.IsFavorite = !Editor.IsFavorite; break;
            case 7: _virtualKeyboard.Open("ROM Path", Editor.RomPath, v => Editor.RomPath = v); break;
            case 8: _virtualKeyboard.Open("Process Name Override", Editor.ProcessNameOverride, v => Editor.ProcessNameOverride = v); break;
            case 9: await BrowseBezelOverrideAsync(); break;
            case 10: OpenNumericKeyboard("Display Width Override (0 = use system default)", Editor.DisplayModeOverrideWidth, v => Editor.DisplayModeOverrideWidth = v); break;
            case 11: OpenNumericKeyboard("Display Height Override (0 = use system default)", Editor.DisplayModeOverrideHeight, v => Editor.DisplayModeOverrideHeight = v); break;
            case 12: OpenNumericKeyboard("Display Refresh Hz Override (0 = use system default)", Editor.DisplayModeOverrideRefreshHz, v => Editor.DisplayModeOverrideRefreshHz = v); break;
            case 13:
                if (Editor.BiosOverridePaths.Count > 0)
                {
                    IsBrowsingBiosOverrides = true;
                    SelectedBiosOverrideIndex = Math.Clamp(SelectedBiosOverrideIndex, 0, Editor.BiosOverridePaths.Count - 1);
                }
                break;
            case 14: await BrowseAddBiosOverrideAsync(); break;
            case 15:
                if (Editor.DisabledDeviceTypeOptions.Count > 0)
                {
                    IsBrowsingDisabledDeviceTypes = true;
                    SelectedDeviceTypeCheckIndex = Math.Clamp(SelectedDeviceTypeCheckIndex, 0, Editor.DisabledDeviceTypeOptions.Count - 1);
                }
                break;
            case 16: _virtualKeyboard.Open("DemulShooter Target", Editor.DemulShooterTarget, v => Editor.DemulShooterTarget = v); break;
            case 19: AddPendingPlayerDeviceAssignment(); break;
            case 20:
                if (Editor.PlayerDeviceAssignmentEntries.Count > 0)
                {
                    IsBrowsingPlayerAssignments = true;
                    SelectedPlayerAssignmentIndex = Math.Clamp(SelectedPlayerAssignmentIndex, 0, Editor.PlayerDeviceAssignmentEntries.Count - 1);
                }
                break;
            case 21: await SaveEditorAsync(); break;
            case 22: CancelEditor(); break;
            // 1, 3, 5 (System/Emulator/Players), 17, 18 (pending assignment player
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
    private ScraperGameMetadata? _lastScrapedMetadata;
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

        _lastScrapedMetadata = details;

        if (!string.IsNullOrWhiteSpace(details.Genre)) Editor.Genre = details.Genre;
        if (details.Players is { } p) Editor.Players = Math.Clamp(p, 1, 8);

        var coverPath = await DownloadToScratchAsync(details.CoverImageUrl, "cover");
        if (coverPath is not null) Editor.CoverPath = coverPath;

        var logoPath = await DownloadToScratchAsync(details.LogoImageUrl, "logo");
        if (logoPath is not null) Editor.LogoPath = logoPath;

        var marqueePath = await DownloadToScratchAsync(details.MarqueeImageUrl, "marquee");
        if (marqueePath is not null) Editor.MarqueePath = marqueePath;

        // Persisted like Cover/Logo/Marquee (via CopyMediaFilesAsync on Save) so the
        // ComfyUI poster-collage generator has real reference material to reuse later
        // without needing to re-scrape every session.
        Editor.ScreenshotPaths.Clear();
        foreach (var screenshotUrl in details.ScreenshotImageUrls.Take(3))
        {
            var screenshotPath = await DownloadToScratchAsync(screenshotUrl, "screenshot");
            if (screenshotPath is not null) Editor.ScreenshotPaths.Add(screenshotPath);
        }

        var platformSuffix = string.IsNullOrWhiteSpace(picked.Platform) ? "" : $" [{picked.Platform}]";
        StatusMessage = $"Match {_scrapeResultIndex + 1}/{_lastScrapeResults.Count}: '{details.Title}'{platformSuffix} on {source} — Left/Right for other matches, review, then Save.";
        _logger.LogInformation("Scraped '{Title}' from {Source} (matched '{MatchedTitle}', {Index}/{Count}).",
            Editor.Title, source, details.Title, _scrapeResultIndex + 1, _lastScrapeResults.Count);
    }

    /// <summary>
    /// Generator #1 ("Clean Cover for Card"): a 4-step pipeline over the actual
    /// scraped Cover Art —
    ///   1) Remove logo/text: a single-image ComfyUI masked-inpainting pass (see
    ///      ScraperSettings.ComfyUiCleanupWorkflowPath) regenerates only the regions
    ///      actually detected as logo/text, pasting the result back onto the untouched
    ///      original everywhere else (ImageCompositeMasked in the workflow). The mask
    ///      itself comes from two local, no-cloud detectors so this works regardless of
    ///      where a given cover happens to put its logo (an earlier fixed-top-band
    ///      version couldn't generalize across covers): LogoRegionDetector template-
    ///      matches the game's own scraped logo asset against the cover to find the
    ///      main title logo wherever it sits, and TextRegionDetector (optional,
    ///      ScraperSettings.TextDetectionModelPath) runs a local ONNX text-detection
    ///      model to catch other stray badges (publisher/platform logos) that aren't
    ///      an asset UGL has on file to match against. Falls back to a fixed top-band
    ///      guess only if neither detector finds anything. Deliberately run at the
    ///      cover's OWN aspect ratio (just rounded to a multiple of 8 for the latent),
    ///      not the card's tall aspect ratio — stretching a ~2:3 cover into a ~1:2.6
    ///      canvas first is what caused whole-image distortion in an earlier version.
    ///   2) Resize (not crop) to the current card resolution.
    ///   3) Upscale/increase detail as needed.
    ///   (steps 2+3 are both just ResizeCoverFit — see its own doc comment)
    ///   4) Composite the real, un-regenerated logo on top (CompositeLogo).
    /// If no cleanup workflow is configured (or ComfyUI is unreachable), step 1 is
    /// skipped and steps 2-4 still run on the plain scraped cover. Result goes to Card
    /// Art, the same slot generator #2 uses — both are just two ways to produce it.
    /// </summary>
    [RelayCommand]
    private async Task ResizeCoverToCardAsync()
    {
        var absoluteCoverPath = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(Editor.CoverPath);
        if (string.IsNullOrWhiteSpace(absoluteCoverPath) || !File.Exists(absoluteCoverPath))
        {
            StatusMessage = "No Cover Art to clean up — scrape or browse for one first.";
            return;
        }

        try
        {
            var coverBytes = await File.ReadAllBytesAsync(absoluteCoverPath);
            var settings = await _scraperSettingsRepo.GetSettingsAsync();
            var absoluteLogoPathForDetection = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(Editor.LogoPath);

            var imageBytes = coverBytes;
            var cleanedUp = false;
            if (!string.IsNullOrWhiteSpace(settings.ComfyUiCleanupWorkflowPath))
            {
                StatusMessage = "Cleaning up cover art via ComfyUI (removing logo/text)… this can take a while.";
                // Deliberately no negation ("no text", "no logo", etc.) — diffusion
                // models (Flux included) are notoriously bad at negation prompting and
                // often render the very thing they're told to exclude just because the
                // word appears in the prompt. Flux Fill's own workflow also runs at
                // cfg=1.0 (see GameCardCleanup_ComfyUI_workflow.json), which makes the
                // negative-prompt channel essentially inert, so there's no fallback
                // channel to suppress it either — the positive prompt has to describe
                // only what should be there.
                const string cleanupPrompt = "a smooth, plain continuation of the surrounding artwork and background colors, blended seamlessly";

                // Step 1 works at the cover's own aspect ratio, not the card's — only
                // rounded up to the nearest multiple of 8 (a hard requirement for
                // SD/SDXL-family latents). The mask must match this exact size, not
                // the final card resolution (that reflow happens afterward, in step
                // 2, once the cleaned image is back).
                int coverWidth, coverHeight;
                using (var probeStream = new MemoryStream(coverBytes))
                using (var probeBitmap = new Bitmap(probeStream))
                {
                    coverWidth = probeBitmap.Width;
                    coverHeight = probeBitmap.Height;
                }
                var workWidth = RoundUpToMultipleOf8(coverWidth);
                var workHeight = RoundUpToMultipleOf8(coverHeight);

                // Detect where the logo/text actually is on THIS cover, in the
                // cover's own original pixel space, then scale into working-canvas
                // space (near 1:1 — workWidth/Height only differ from
                // coverWidth/Height by up to 7px of multiple-of-8 rounding).
                var detectedRegions = new List<System.Drawing.Rectangle>();
                if (!string.IsNullOrWhiteSpace(absoluteLogoPathForDetection) && File.Exists(absoluteLogoPathForDetection))
                {
                    try
                    {
                        var logoBytesForDetection = await File.ReadAllBytesAsync(absoluteLogoPathForDetection);
                        var match = LogoRegionDetector.LocateLogo(coverBytes, logoBytesForDetection);
                        if (match is not null)
                        {
                            detectedRegions.Add(match.Value.Bounds);
                            _logger.LogInformation("Logo template match found at {Bounds} (confidence {Confidence:F2}).", match.Value.Bounds, match.Value.Confidence);
                        }
                        else
                        {
                            _logger.LogInformation("Logo template match found nothing above the confidence threshold.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Logo template-match against the cover failed — continuing without it.");
                    }
                }
                if (!string.IsNullOrWhiteSpace(settings.TextDetectionModelPath) && File.Exists(settings.TextDetectionModelPath))
                {
                    var textRegions = TextRegionDetector.DetectTextRegions(coverBytes, settings.TextDetectionModelPath, _logger);
                    _logger.LogInformation("Text detection found {Count} region(s): {Regions}", textRegions.Count, string.Join(", ", textRegions));
                    detectedRegions.AddRange(textRegions);
                }

                double regionScaleX = (double)workWidth / coverWidth;
                double regionScaleY = (double)workHeight / coverHeight;
                var scaledRegions = detectedRegions.Select(r => new System.Drawing.Rectangle(
                    (int)(r.X * regionScaleX), (int)(r.Y * regionScaleY),
                    (int)(r.Width * regionScaleX), (int)(r.Height * regionScaleY)));

                var maskBytes = scaledRegions.Any()
                    ? BuildRegionsMask(workWidth, workHeight, scaledRegions)
                    : BuildTopBandMask(workWidth, workHeight, CleanupMaskBandFraction);

                // 1.0 is Flux Fill's own documented default (see
                // GameCardCleanup_ComfyUI_workflow.json) — its InpaintModelConditioning
                // architecture handles unmasked-region preservation directly, unlike
                // the older SD1.5 VAEEncodeForInpaint approach that relied on a
                // partial denoise value to avoid a full repaint.
                var cleaned = await _comfyUiClient.GenerateImageAsync(
                    cleanupPrompt, [coverBytes, maskBytes], denoise: 1.0,
                    workflowPathOverride: settings.ComfyUiCleanupWorkflowPath,
                    extraNumericTokens: new Dictionary<string, double>
                    {
                        ["{{WORK_WIDTH}}"] = workWidth,
                        ["{{WORK_HEIGHT}}"] = workHeight,
                    });
                if (cleaned is not null)
                {
                    imageBytes = cleaned;
                    cleanedUp = true;
                }
                else
                {
                    _logger.LogWarning("ComfyUI cleanup pass failed — falling back to a plain local resize of the cover.");
                }
            }

            // Steps 2+3: resize (not crop) to the card size, upscaling/increasing
            // detail as needed — see ResizeCoverFit's own doc comment.
            var (width, height) = GetTargetCardResolution();
            imageBytes = ResizeCoverFit(imageBytes, width, height);

            var logoComposited = false;
            if (!string.IsNullOrWhiteSpace(absoluteLogoPathForDetection) && File.Exists(absoluteLogoPathForDetection))
            {
                try
                {
                    imageBytes = CompositeLogo(imageBytes, absoluteLogoPathForDetection);
                    logoComposited = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to composite the game logo onto the cleaned-up card art.");
                }
            }

            Directory.CreateDirectory(ScrapeScratchDir);
            var scratchPath = Path.Combine(ScrapeScratchDir, $"cardresize_{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(scratchPath, imageBytes);
            Editor.CardArtPath = scratchPath;

            var basis = cleanedUp ? "cleaned-up Cover Art" : "Cover Art (no cleanup workflow configured — plain resize only)";
            StatusMessage = logoComposited
                ? $"✓ Card art produced from {basis} at {width}×{height}, with logo overlay. Review it, then Save."
                : $"✓ Card art produced from {basis} at {width}×{height} (no logo overlay — none was scraped for this game). Review it, then Save.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to produce card art from Cover Art.");
            StatusMessage = "Failed to produce card art from Cover Art — see logs.";
        }
    }

    /// <summary>
    /// Generator #2: generates card art via the configured ComfyUI workflow, using the
    /// game's Title/Genre as the prompt plus up to 3 of the most recently scraped
    /// cover/screenshot images as collage source material (so the result is a
    /// literal collage of the game's actual imagery, not a reinterpretation). The
    /// game's actual logo (Editor.LogoPath, from the most recent scrape) is then
    /// composited on top — pasted as-is rather than regenerated, since diffusion
    /// models can't reliably reproduce exact logo art/text — and applies the result
    /// as the Card Art field, a separate asset from Cover Art (like ScrapeAsync, the
    /// actual file copy happens on Save — this just points Editor.CardArtPath at a
    /// downloaded scratch file).
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
            ? $"a cohesive video game poster blending the exact characters and scenes shown into one image for \"{Editor.Title}\". Do not invent new characters or scenes not shown. Do not add any text, logos, or watermarks."
            : $"a cohesive video game poster blending the exact characters and scenes shown into one image for \"{Editor.Title}\", a {Editor.Genre} game. Do not invent new characters or scenes not shown. Do not add any text, logos, or watermarks.";

        var referenceImages = await DownloadReferenceImagesAsync();

        StatusMessage = referenceImages.Count > 0
            ? $"Generating card art via ComfyUI using {referenceImages.Count} reference image(s)… this can take a while."
            : "No scraped reference images available — generating from text prompt alone via ComfyUI… this can take a while.";
        var imageBytes = await _comfyUiClient.GenerateImageAsync(prompt, referenceImages, denoise: 0.35);
        if (imageBytes is null)
        {
            StatusMessage = "ComfyUI generation failed — check logs\\ugl.log for the actual error (endpoint unreachable, workflow rejected, no {{PROMPT}} token, or a timed-out render).";
            return;
        }

        // Force the actual current card resolution regardless of whatever the
        // workflow's own internal sampling/output resolution happens to be — the
        // workflow file would otherwise need to be kept in sync by hand every time
        // the card-grid size changes (different window size, theme, etc.).
        var (targetWidth, targetHeight) = GetTargetCardResolution();
        imageBytes = ResizeCoverFit(imageBytes, targetWidth, targetHeight);

        var logoComposited = false;
        // LogoPath from a previously-saved game is stored "portable" (relative to the
        // app's own folder) — must resolve it the same way the rest of the app does
        // before touching the filesystem, or a real, already-scraped logo silently
        // reads as "missing" (this was the NFL Blitz 2001 case: logo scraped fine,
        // but File.Exists on the raw relative path failed).
        var absoluteLogoPath = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(Editor.LogoPath);
        if (!string.IsNullOrWhiteSpace(absoluteLogoPath) && File.Exists(absoluteLogoPath))
        {
            try
            {
                imageBytes = CompositeLogo(imageBytes, absoluteLogoPath);
                logoComposited = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to composite the game logo onto the generated card art.");
            }
        }

        try
        {
            Directory.CreateDirectory(ScrapeScratchDir);
            var scratchPath = Path.Combine(ScrapeScratchDir, $"comfyui_{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(scratchPath, imageBytes);
            Editor.CardArtPath = scratchPath;
            // Both branches are a success — Card Art was generated either way. The
            // second just notes the logo overlay was skipped, since a status message
            // starting with "no logo" reads like an error to a quick glance even
            // though generation fully succeeded.
            StatusMessage = logoComposited
                ? "✓ Card art generated with logo overlay. Review it, then Save."
                : "✓ Card art generated successfully (no logo overlay — none was scraped for this game, or your source doesn't provide one; ScreenScraper/TheGamesDB can, IGDB can't). Review it, then Save.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save ComfyUI-generated image to the scratch folder.");
            StatusMessage = "Generated the image but failed to save it locally — see logs.";
        }
    }

    /// <summary>Default card resolution used only when <see cref="CardDimensionInfo"/>
    /// hasn't measured anything yet this session (e.g. Generate/Resize used before
    /// ever browsing a category's game grid).</summary>
    private const int FallbackCardWidth = 756;
    private const int FallbackCardHeight = 1968;

    /// <summary>The resolution both card-art generators should target — the actual
    /// live on-screen size of a rendered game card (device pixels, DPI-scaled) when
    /// available, since that's the true "best size" for this window/theme, falling
    /// back to a fixed default only if no card has rendered yet this session.</summary>
    private static (int Width, int Height) GetTargetCardResolution()
    {
        var measured = CardDimensionInfo.GameCardPixelSize;
        return measured.Width > 0 && measured.Height > 0
            ? ((int)Math.Round(measured.Width), (int)Math.Round(measured.Height))
            : (FallbackCardWidth, FallbackCardHeight);
    }

    /// <summary>Rounds up to the nearest multiple of 8 — SD/SDXL-family latents require
    /// both dimensions to be multiples of 8, so the cleanup workflow's working canvas
    /// (the cover's own size, not the card's) has to be snapped to one.</summary>
    private static int RoundUpToMultipleOf8(int value) => (int)(Math.Ceiling(value / 8.0) * 8);

    /// <summary>Fraction of the working canvas height that "Clean Cover for Card"
    /// clears via masked inpainting — matches CompositeLogo's own band so the region
    /// the ComfyUI cleanup pass regenerates is exactly the region the real logo will
    /// be pasted over afterward.</summary>
    private const double CleanupMaskBandFraction = 0.28;

    /// <summary>Builds a black/white mask image (white = regenerate, black = leave
    /// untouched) covering the top <paramref name="bandFraction"/> of the canvas, with
    /// a soft feathered gradient at the band's lower edge so the ComfyUI inpainting
    /// pass doesn't leave a hard seam where the regenerated band meets the original
    /// artwork. Uploaded to ComfyUI as a second reference image and converted to an
    /// actual MASK by the cleanup workflow's own ImageToMask node.</summary>
    private static byte[] BuildTopBandMask(int width, int height, double bandFraction)
    {
        using var mask = new Bitmap(width, height);
        using (var g = Graphics.FromImage(mask))
        {
            g.Clear(Color.Black);

            float bandHeight = (float)(height * bandFraction);
            float featherHeight = bandHeight * 0.25f;
            float solidHeight = bandHeight - featherHeight;

            g.FillRectangle(Brushes.White, 0, 0, width, solidHeight);

            using var brush = new LinearGradientBrush(
                new RectangleF(0, solidHeight, width, featherHeight),
                Color.White, Color.Black, LinearGradientMode.Vertical);
            g.FillRectangle(brush, 0, solidHeight, width, featherHeight);
        }

        using var outStream = new MemoryStream();
        mask.Save(outStream, ImageFormat.Png);
        return outStream.ToArray();
    }

    /// <summary>Builds a black/white mask image (white = regenerate, black = leave
    /// untouched) covering the given detected logo/text regions — the
    /// detection-driven counterpart to <see cref="BuildTopBandMask"/>, used whenever
    /// LogoRegionDetector/TextRegionDetector actually found something, so the
    /// cleanup pass only ever touches where a given cover's logo/text really is,
    /// rather than a guessed fixed band. Each region gets a small extra pad (on top
    /// of whatever padding the detector itself already applied) since a mask that's a
    /// hair too small leaves a visible sliver of the original logo/text behind.
    ///
    /// The whole mask is then given a soft blur so there's no hard seam at any
    /// region's edge — via a real area-averaged downscale (Graphics.DrawImage with
    /// HighQualityBicubic), NOT the plain `new Bitmap(src, w, h)` constructor
    /// <see cref="BuildBlurredCoverBackground"/> uses for full-frame photographic
    /// content: that constructor's default resampling can flat-out miss an isolated
    /// small-to-medium rectangle on an otherwise solid-black canvas (it doesn't average
    /// over each output pixel's source block), which silently produced a near-empty
    /// mask and made an earlier version of this method regenerate almost nothing at
    /// all. A modest blur radius (not an aggressive 20x downscale) keeps enough
    /// resolution that even a fairly small detected region survives intact.</summary>
    private static byte[] BuildRegionsMask(int width, int height, IEnumerable<System.Drawing.Rectangle> regions)
    {
        using var mask = new Bitmap(width, height);
        using (var g = Graphics.FromImage(mask))
        {
            g.Clear(Color.Black);
            // Wider than the original 0.15 — testing showed text sitting on a busy,
            // non-uniform background (e.g. baked into a graphic's own gradient/shape)
            // needs more surrounding context erased for the model to have room to
            // reconstruct a coherent region, rather than "completing" a stray edge of
            // the original glyph strokes it can still see just outside a tight mask.
            const float extraPadFraction = 0.35f;
            foreach (var region in regions)
            {
                int padX = (int)(region.Width * extraPadFraction);
                int padY = (int)(region.Height * extraPadFraction);
                int x = Math.Max(0, region.X - padX);
                int y = Math.Max(0, region.Y - padY);
                int w = Math.Min(width - x, region.Width + padX * 2);
                int h = Math.Min(height - y, region.Height + padY * 2);
                if (w <= 0 || h <= 0) continue;
                g.FillRectangle(Brushes.White, x, y, w, h);
            }
        }

        const int blurDownscaleFactor = 6;
        int smallWidth = Math.Max(1, width / blurDownscaleFactor);
        int smallHeight = Math.Max(1, height / blurDownscaleFactor);
        using var shrunk = new Bitmap(smallWidth, smallHeight);
        using (var g = Graphics.FromImage(shrunk))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(mask, 0, 0, smallWidth, smallHeight);
        }
        using var blurred = new Bitmap(width, height);
        using (var g = Graphics.FromImage(blurred))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(shrunk, 0, 0, width, height);
        }

        using var outStream = new MemoryStream();
        blurred.Save(outStream, ImageFormat.Png);
        return outStream.ToArray();
    }

    /// <summary>Reconstructs the source image at the target dimensions without losing
    /// any content: the full source is scaled (up or down, high-quality bicubic — this
    /// is where low-res covers get "increased resolution if needed") to fit entirely
    /// inside the target box, then centered over a softly blurred, cover-fit version of
    /// the same image stretched to fill the rest of the canvas. Unlike a hard center-crop,
    /// nothing from the original image is cut off — the target's leftover space (from the
    /// aspect-ratio mismatch between a typical box cover and a tall card) is filled with
    /// context from the same artwork instead of a blank bar or a chopped-off edge.</summary>
    private static byte[] ResizeCoverFit(byte[] imageBytes, int targetWidth, int targetHeight)
    {
        using var sourceStream = new MemoryStream(imageBytes);
        using var source = new Bitmap(sourceStream);

        using var result = new Bitmap(targetWidth, targetHeight);
        using (var g = Graphics.FromImage(result))
        {
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background: cover-fit (fills the whole canvas, cropping overflow) then
            // blurred, so the leftover space around the contained foreground reads as
            // an extension of the artwork rather than a hard crop or empty bar.
            using (var background = BuildBlurredCoverBackground(source, targetWidth, targetHeight))
            {
                g.DrawImage(background, 0, 0, targetWidth, targetHeight);
            }

            // Foreground: contain-fit (the entire source, no cropping), centered on top.
            double containScale = Math.Min((double)targetWidth / source.Width, (double)targetHeight / source.Height);
            int drawWidth = (int)Math.Round(source.Width * containScale);
            int drawHeight = (int)Math.Round(source.Height * containScale);
            int drawX = (targetWidth - drawWidth) / 2;
            int drawY = (targetHeight - drawHeight) / 2;
            g.DrawImage(source, new Rectangle(drawX, drawY, drawWidth, drawHeight));
        }

        using var outStream = new MemoryStream();
        result.Save(outStream, ImageFormat.Png);
        return outStream.ToArray();
    }

    /// <summary>Builds a cover-fit (crop-to-fill), softly blurred backdrop from the
    /// source image at the target size — used to pad out the frame behind a contain-fit
    /// foreground. The blur is a cheap downscale/upscale trick (no Gaussian kernel needed):
    /// shrinking the cover-fit image destroys fine detail, and the following high-quality
    /// bicubic upscale spreads what remains into a smooth blur.</summary>
    private static Bitmap BuildBlurredCoverBackground(Bitmap source, int targetWidth, int targetHeight)
    {
        double coverScale = Math.Max((double)targetWidth / source.Width, (double)targetHeight / source.Height);
        int coveredWidth = (int)Math.Ceiling(source.Width * coverScale);
        int coveredHeight = (int)Math.Ceiling(source.Height * coverScale);
        int offsetX = (coveredWidth - targetWidth) / 2;
        int offsetY = (coveredHeight - targetHeight) / 2;

        using var covered = new Bitmap(targetWidth, targetHeight);
        using (var g = Graphics.FromImage(covered))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(-offsetX, -offsetY, coveredWidth, coveredHeight));
        }

        const int blurDownscaleFactor = 12;
        int smallWidth = Math.Max(1, targetWidth / blurDownscaleFactor);
        int smallHeight = Math.Max(1, targetHeight / blurDownscaleFactor);
        using var shrunk = new Bitmap(covered, smallWidth, smallHeight);

        var blurred = new Bitmap(targetWidth, targetHeight);
        using (var g = Graphics.FromImage(blurred))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(shrunk, 0, 0, targetWidth, targetHeight);
        }
        return blurred;
    }

    /// <summary>Pastes the game's actual logo image (not regenerated — diffusion
    /// models can't reliably reproduce exact logo art/text) onto the generated poster,
    /// scaled to fit within the top 25% of the card and centered (both axes) within
    /// that band, with a soft drop shadow for legibility against busy artwork.</summary>
    private static byte[] CompositeLogo(byte[] posterBytes, string logoPath)
    {
        using var posterStream = new MemoryStream(posterBytes);
        using var poster = new Bitmap(posterStream);
        using var logo = new Bitmap(logoPath);

        const double bandHeightFraction = 0.25;
        const double maxWidthFraction = 0.85;
        const double bandPaddingFraction = 0.02;

        double bandHeight = poster.Height * bandHeightFraction;
        double maxWidth = poster.Width * maxWidthFraction;
        double maxHeight = bandHeight * (1 - 2 * bandPaddingFraction);
        double scale = Math.Min(maxWidth / logo.Width, maxHeight / logo.Height);

        int logoWidth = (int)(logo.Width * scale);
        int logoHeight = (int)(logo.Height * scale);
        int x = (poster.Width - logoWidth) / 2;
        int y = (int)((bandHeight - logoHeight) / 2);

        using var g = Graphics.FromImage(poster);
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Soft drop shadow: a black silhouette of the logo, offset and semi-transparent,
        // drawn first so the logo itself sits on top.
        var shadowMatrix = new ColorMatrix(
        [
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0.5f, 0],
            [0, 0, 0, 0, 1],
        ]);
        using (var shadowAttributes = new ImageAttributes())
        {
            shadowAttributes.SetColorMatrix(shadowMatrix);
            const int shadowOffset = 6;
            g.DrawImage(logo, new Rectangle(x + shadowOffset, y + shadowOffset, logoWidth, logoHeight),
                0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, shadowAttributes);
        }

        g.DrawImage(logo, new Rectangle(x, y, logoWidth, logoHeight));

        using var outStream = new MemoryStream();
        poster.Save(outStream, ImageFormat.Png);
        return outStream.ToArray();
    }

    /// <summary>Picks up to 3 URLs — the main cover first (the hero image for the
    /// collage), then screenshots, then alternate covers/artwork as filler — from the
    /// most recently scraped metadata and downloads their raw bytes for use as ComfyUI
    /// collage source images. Individual read/download failures are skipped rather
    /// than aborting the whole card generation.
    ///
    /// Reads the game's own persisted local files first (Cover, then Screenshots —
    /// both saved like any other media asset since screenshots started being
    /// persisted on scrape/save) rather than re-downloading by URL. This is what
    /// makes regenerating a previously-saved game's card art work without
    /// re-scraping first: the reference material survives a save/reopen cycle the
    /// same way Cover/Logo/Marquee already did. Falls back to downloading
    /// alternate-artwork URLs from the most recent in-session scrape only as filler
    /// when local files alone don't reach 3 images (artwork isn't persisted as its
    /// own asset).</summary>
    private async Task<List<byte[]>> DownloadReferenceImagesAsync()
    {
        var result = new List<byte[]>();

        var localPaths = new[] { Editor.CoverPath }.Concat(Editor.ScreenshotPaths);
        foreach (var localPath in localPaths)
        {
            if (result.Count >= 3) break;
            if (string.IsNullOrWhiteSpace(localPath)) continue;
            var absolutePath = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(localPath);
            if (!File.Exists(absolutePath)) continue;
            try
            {
                result.Add(await File.ReadAllBytesAsync(absolutePath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read local reference image {Path} for card generation.", absolutePath);
            }
        }

        if (result.Count < 3 && _lastScrapedMetadata is { } meta)
        {
            foreach (var url in meta.ArtworkImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
            {
                if (result.Count >= 3) break;
                try
                {
                    result.Add(await _imageDownloadClient.GetByteArrayAsync(url));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download reference image from {Url} for card generation.", url);
                }
            }
        }

        return result;
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
        SelectSettingsTab();
        SettingsFocusIndex = 0;
        ArtFocusIndex = 0;
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
        _lastScrapedMetadata = null;
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedGame is null) return;
        _lastScrapedMetadata = null;
        Editor.PopulateFrom(SelectedGame);
        Editor.SyncPlayerDeviceAssignments(SelectedGame.PlayerDeviceAssignments, _peripheralRegistry.KnownDevices);
        IsEditorOpen = true;
        SelectSettingsTab();
        SettingsFocusIndex = 0;
        ArtFocusIndex = 0;
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
    private Task DeleteSelectedAsync()
    {
        if (SelectedGame is null) return Task.CompletedTask;
        _confirmDialog.Open($"Delete '{SelectedGame.Title}'? This cannot be undone.", () => _ = PerformDeleteSelectedAsync());
        return Task.CompletedTask;
    }

    private async Task PerformDeleteSelectedAsync()
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
    private async Task BrowseCardArtAsync()
        => Editor.CardArtPath = await BrowseImageAsync() ?? Editor.CardArtPath;

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
    /// fieldName: "cover", "background", "logo", "video", "marquee", "cardart"
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
            case "cardart":    Editor.CardArtPath     = filePath; break;
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
        var cardArt    = await CopyMediaFileAsync(game, game.Media.CardArtPath,    "cardart",     mediaRoot);

        // Screenshots: no single-file precedent to reuse — CopyMediaFileAsync's
        // optional suffix param gives each of up to 3 its own filename
        // ({slug}-1.ext, -2.ext, -3.ext) in a shared "screenshots" subfolder.
        var screenshots = new List<string>();
        for (int i = 0; i < game.Media.ScreenshotPaths.Count; i++)
        {
            var copied = await CopyMediaFileAsync(game, game.Media.ScreenshotPaths[i], "screenshots", mediaRoot, suffix: $"-{i + 1}");
            screenshots.Add(copied ?? game.Media.ScreenshotPaths[i]);
        }

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
                CardArtPath    = cardArt    ?? game.Media.CardArtPath,
                PreferCardArtAsCover = game.Media.PreferCardArtAsCover,
                ScreenshotPaths = screenshots,
            }
        };
    }

    private async Task<string?> CopyMediaFileAsync(
        Game game, string sourcePath, string subfolder, string mediaRoot, string suffix = "")
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
        var destPath = Path.Combine(destDir, slug + suffix + ext);

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
