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
    private readonly MediaAssetResolver _resolver;
    private readonly SkiaMediaCache _cache;
    private readonly VirtualKeyboardViewModel _virtualKeyboard;
    private readonly ILogger<GamesConfigViewModel> _logger;

    /// <summary>
    /// Field position while the editor is open — 0=Title, 4=Genre, 7-12=media/ROM paths
    /// (text, open the virtual keyboard), 13=Save, 14=Cancel. System(1)/Emulator(3) cycle
    /// via Left/Right, Players(5) adjusts via Left/Right, Favorite(6) toggles via Confirm.
    /// Category(2) is a multi-select checkbox grid — Confirm enters a sub-mode (see
    /// IsCategoryOptionsFocused below), matching the same "enter list, Back exits it"
    /// convention as Audio's track sub-list.
    /// </summary>
    [ObservableProperty] private int _editorFocusIndex;
    private const int EditorPositionCount = 18;

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

    // ── BIOS override sub-mode — entered via Confirm at EditorFocusIndex==14 ──────────
    // Nearly always empty; only needed for the rare game that requires a BIOS
    // different from its emulator's own default list (Emulator.BiosPaths). Plain
    // strings, not a wrapper VM — the ListBox's own SelectedIndex binding drives the
    // highlight directly, same pattern as Audio's track list.
    [ObservableProperty] private bool _isBrowsingBiosOverrides;
    [ObservableProperty] private int _selectedBiosOverrideIndex;

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
    public bool IsCoverPathFocused      => EditorFocusIndex == 8;
    public bool IsBackgroundPathFocused => EditorFocusIndex == 9;
    public bool IsLogoPathFocused       => EditorFocusIndex == 10;
    public bool IsVideoPathFocused      => EditorFocusIndex == 11;
    public bool IsMarqueePathFocused    => EditorFocusIndex == 12;
    public bool IsBezelOverrideFocused    => EditorFocusIndex == 13;
    public bool IsBiosOverrideListFocused => EditorFocusIndex == 14;
    public bool IsAddBiosOverrideFocused  => EditorFocusIndex == 15;
    public bool IsSaveFocused           => EditorFocusIndex == 16;
    public bool IsCancelFocused         => EditorFocusIndex == 17;

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
        OnPropertyChanged(nameof(IsCoverPathFocused));
        OnPropertyChanged(nameof(IsBackgroundPathFocused));
        OnPropertyChanged(nameof(IsLogoPathFocused));
        OnPropertyChanged(nameof(IsVideoPathFocused));
        OnPropertyChanged(nameof(IsMarqueePathFocused));
        OnPropertyChanged(nameof(IsBezelOverrideFocused));
        OnPropertyChanged(nameof(IsBiosOverrideListFocused));
        OnPropertyChanged(nameof(IsAddBiosOverrideFocused));
        OnPropertyChanged(nameof(IsSaveFocused));
        OnPropertyChanged(nameof(IsCancelFocused));
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
        MediaAssetResolver resolver,
        SkiaMediaCache cache,
        VirtualKeyboardViewModel virtualKeyboard,
        ILogger<GamesConfigViewModel> logger)
    {
        _gameRepo = gameRepo;
        _config = config;
        _emulatorRepo = emulatorRepo;
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
        if (!IsEditorOpen || IsBrowsingBiosOverrides) return;
        switch (EditorFocusIndex)
        {
            case 1: CycleSystem(-1); break;
            case 3: CycleEmulator(-1); break;
            case 5: Editor.Players = Math.Max(1, Editor.Players - 1); break;
        }
    }

    public void NavigateRight()
    {
        if (!IsEditorOpen || IsBrowsingBiosOverrides) return;
        switch (EditorFocusIndex)
        {
            case 1: CycleSystem(1); break;
            case 3: CycleEmulator(1); break;
            case 5: Editor.Players = Math.Min(8, Editor.Players + 1); break;
        }
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
            case 8: _virtualKeyboard.Open("Cover Path", Editor.CoverPath, v => Editor.CoverPath = v); break;
            case 9: _virtualKeyboard.Open("Background Path", Editor.BackgroundPath, v => Editor.BackgroundPath = v); break;
            case 10: _virtualKeyboard.Open("Logo Path", Editor.LogoPath, v => Editor.LogoPath = v); break;
            case 11: _virtualKeyboard.Open("Video Path", Editor.VideoPath, v => Editor.VideoPath = v); break;
            case 12: _virtualKeyboard.Open("Marquee Path", Editor.MarqueePath, v => Editor.MarqueePath = v); break;
            case 13: await BrowseBezelOverrideAsync(); break;
            case 14:
                if (Editor.BiosOverridePaths.Count > 0)
                {
                    IsBrowsingBiosOverrides = true;
                    SelectedBiosOverrideIndex = Math.Clamp(SelectedBiosOverrideIndex, 0, Editor.BiosOverridePaths.Count - 1);
                }
                break;
            case 15: await BrowseAddBiosOverrideAsync(); break;
            case 16: await SaveEditorAsync(); break;
            case 17: CancelEditor(); break;
            case 6: Editor.IsFavorite = !Editor.IsFavorite; break;
            // 1, 3, 5 (System/Emulator/Players) are adjusted via Left/Right, not Confirm.
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
        Editor.Id = string.Empty; // Force new-game Id generation on save
        IsEditorOpen = true;
        EditorFocusIndex = 0;
        IsCategoryOptionsFocused = false;
        SelectedCategoryOptionIndex = 0;
        IsBrowsingBiosOverrides = false;
        SelectedBiosOverrideIndex = 0;
        SelectedGame = null;
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (SelectedGame is null) return;
        Editor.PopulateFrom(SelectedGame);
        IsEditorOpen = true;
        EditorFocusIndex = 0;
        IsCategoryOptionsFocused = false;
        SelectedCategoryOptionIndex = 0;
        IsBrowsingBiosOverrides = false;
        SelectedBiosOverrideIndex = 0;
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
            EmulatorId = game.EmulatorId, Genre = game.Genre,
            Players = game.Players, IsFavorite = game.IsFavorite,
            BezelOverridePath = game.BezelOverridePath,
            BiosOverridePaths = game.BiosOverridePaths,
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

        // Stored relative to the app's own folder when possible, so this keeps
        // working if the whole portable install moves to a different drive letter
        // or machine — MediaAssetResolver.ResolvePath already resolves either form
        // back to an absolute path when loading.
        return UGL.Core.Utilities.PortablePathHelper.ToPortablePath(destPath);
    }
}
