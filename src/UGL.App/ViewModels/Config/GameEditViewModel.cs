using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

/// <summary>One selectable category checkbox in the game editor's category list.</summary>
public sealed partial class CategoryCheckItem : ObservableObject
{
    public string Id { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isChecked;

    /// <summary>Controller-focus indicator — which checkbox is currently highlighted
    /// for Up/Down navigation, distinct from IsChecked (the actual persisted state).</summary>
    [ObservableProperty] private bool _isHighlighted;

    public CategoryCheckItem(string id, string label)
    {
        Id = id;
        Label = label;
    }
}

/// <summary>One selectable peripheral-type checkbox in the game editor's disabled-
/// peripherals list — checking it means this type is silently ignored while this
/// game is running (see ProcessEmulatorLauncher/RawInputService).</summary>
public sealed partial class DeviceTypeCheckItem : ObservableObject
{
    public RawInputDeviceType Type { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isHighlighted;

    public DeviceTypeCheckItem(RawInputDeviceType type, string label)
    {
        Type = type;
        Label = label;
    }
}

/// <summary>One (PlayerIndex, device) entry in the game editor's player-device
/// assignment list. Entries sharing the same PlayerIndex are ranked by their order
/// in the flat list (first = most preferred) — see GameEditViewModel.
/// BuildPlayerDeviceAssignments().</summary>
public sealed partial class PlayerDeviceAssignmentEntry : ObservableObject
{
    public int PlayerIndex { get; }
    public string HardwarePath { get; }
    public string FriendlyName { get; }
    [ObservableProperty] private bool _isHighlighted;

    public PlayerDeviceAssignmentEntry(int playerIndex, string hardwarePath, string friendlyName)
    {
        PlayerIndex = playerIndex;
        HardwarePath = hardwarePath;
        FriendlyName = friendlyName;
    }
}

/// <summary>
/// Editable form backing model for a single game.
/// Populated from an existing Game on edit, or blank on add.
/// Call ToGame() to produce a validated Game record to persist.
/// </summary>
public sealed partial class GameEditViewModel : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _selectedSystemId = string.Empty;
    [ObservableProperty] private string _selectedEmulatorId = string.Empty;
    [ObservableProperty] private string _romPath = string.Empty;

    /// <summary>Only meaningful for a direct-launch Emulator (Steam/Epic/GOG Galaxy) —
    /// see Game.ProcessNameOverride. Nearly always empty.</summary>
    [ObservableProperty] private string _processNameOverride = string.Empty;

    [ObservableProperty] private string _genre = string.Empty;
    [ObservableProperty] private int _players = 1;
    [ObservableProperty] private bool _isFavorite;

    /// <summary>
    /// One checkbox per known category — a game can belong to any number of them
    /// (e.g. both Racing and Multiplayer), and shows up when browsing either.
    /// Populated by GamesConfigViewModel from the full category list.
    /// </summary>
    public ObservableCollection<CategoryCheckItem> CategoryOptions { get; } = [];

    public IReadOnlyList<string> SelectedCategoryIds =>
        CategoryOptions.Where(c => c.IsChecked).Select(c => c.Id).ToList();

    // Media paths (absolute, copied to media/ on save)
    [ObservableProperty] private string _coverPath = string.Empty;
    [ObservableProperty] private string _backgroundPath = string.Empty;
    [ObservableProperty] private string _logoPath = string.Empty;
    [ObservableProperty] private string _videoPath = string.Empty;
    [ObservableProperty] private string _marqueePath = string.Empty;

    /// <summary>AI-generated poster/collage card art — a separate asset from
    /// CoverPath, populated by GamesConfigViewModel.GenerateCardAsync and stored in
    /// its own media/cardart/ subfolder on save.</summary>
    [ObservableProperty] private string _cardArtPath = string.Empty;

    /// <summary>When true, the browse grid displays CardArtPath instead of CoverPath
    /// for this game (see MediaAssetResolver.ResolveCover) — both remain
    /// independently editable regardless of this flag.</summary>
    [ObservableProperty] private bool _preferCardArtAsCover;

    /// <summary>Up to 3 scraped screenshots, persisted like Cover/Logo/Marquee so the
    /// ComfyUI poster-collage generator can reuse them without re-scraping every
    /// session. Populated by GamesConfigViewModel's scrape flow.</summary>
    public ObservableCollection<string> ScreenshotPaths { get; } = [];

    // Live thumbnails for the Art sub-tab, refreshed whenever the corresponding path
    // changes (scrape, browse, drag-drop, or GenerateCardAsync completing). These are
    // arbitrary local file paths, not resolver-based game media, so each is loaded
    // directly from disk rather than through SkiaMediaCache/MediaAssetResolver.
    [ObservableProperty] private Bitmap? _coverPreview;
    [ObservableProperty] private Bitmap? _backgroundPreview;
    [ObservableProperty] private Bitmap? _logoPreview;
    [ObservableProperty] private Bitmap? _marqueePreview;
    [ObservableProperty] private Bitmap? _cardArtPreview;

    partial void OnCoverPathChanged(string value) => CoverPreview = LoadPreview(value, CoverPreview);
    partial void OnBackgroundPathChanged(string value) => BackgroundPreview = LoadPreview(value, BackgroundPreview);
    partial void OnLogoPathChanged(string value) => LogoPreview = LoadPreview(value, LogoPreview);
    partial void OnMarqueePathChanged(string value) => MarqueePreview = LoadPreview(value, MarqueePreview);
    partial void OnCardArtPathChanged(string value) => CardArtPreview = LoadPreview(value, CardArtPreview);

    /// <summary>Disposes the previous preview bitmap (if any) and loads the new one
    /// from disk. Returns null on a missing/empty/unreadable path rather than
    /// throwing — this runs on every keystroke while typing a path manually.
    /// Paths coming from a saved Game (via PopulateFrom) are stored "portable" —
    /// relative to the app's own folder — by PortablePathHelper.ToPortablePath, so
    /// they must be resolved back to absolute before touching the filesystem; a
    /// scratch path from a fresh scrape/generate is already absolute and passes
    /// through ToAbsolutePath unchanged.</summary>
    private static Bitmap? LoadPreview(string path, Bitmap? previous)
    {
        previous?.Dispose();
        if (string.IsNullOrWhiteSpace(path)) return null;
        var absolutePath = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(path);
        if (!File.Exists(absolutePath)) return null;
        try
        {
            using var stream = File.OpenRead(absolutePath);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Rare per-game override of the system's default bezel image. Empty in
    /// the overwhelming majority of cases.</summary>
    [ObservableProperty] private string _bezelOverridePath = string.Empty;

    /// <summary>Rare per-game override of the system's default display mode. Each
    /// field independently optional — 0 means "not set" in the editor's numeric
    /// fields, translated to null when building the Game (see ToGame()).</summary>
    [ObservableProperty] private int _displayModeOverrideWidth;
    [ObservableProperty] private int _displayModeOverrideHeight;
    [ObservableProperty] private int _displayModeOverrideRefreshHz;

    /// <summary>DemulShooter's per-title "-target=" argument. Nearly always empty —
    /// only lightgun games behind DemulShooter need it.</summary>
    [ObservableProperty] private string _demulShooterTarget = string.Empty;

    // Read-only display text for the fields above (set via the virtual keyboard) —
    // see SystemsConfigViewModel's equivalent for why these aren't bound directly.
    public string DisplayModeOverrideWidthText     => DisplayModeOverrideWidth     == 0 ? "Not set" : DisplayModeOverrideWidth.ToString();
    public string DisplayModeOverrideHeightText    => DisplayModeOverrideHeight    == 0 ? "Not set" : DisplayModeOverrideHeight.ToString();
    public string DisplayModeOverrideRefreshHzText => DisplayModeOverrideRefreshHz == 0 ? "Not set" : DisplayModeOverrideRefreshHz.ToString();

    partial void OnDisplayModeOverrideWidthChanged(int value) => OnPropertyChanged(nameof(DisplayModeOverrideWidthText));
    partial void OnDisplayModeOverrideHeightChanged(int value) => OnPropertyChanged(nameof(DisplayModeOverrideHeightText));
    partial void OnDisplayModeOverrideRefreshHzChanged(int value) => OnPropertyChanged(nameof(DisplayModeOverrideRefreshHzText));

    /// <summary>Rare per-game override of the emulator's default BIOS file list. Empty
    /// in the overwhelming majority of cases.</summary>
    public ObservableCollection<string> BiosOverridePaths { get; } = [];

    /// <summary>
    /// One checkbox per assignable peripheral type (the same set RawInputService
    /// considers meaningful to assign a player index to — Gamepad, Lightgun, Wheel,
    /// Spinner, Trackball; not Unknown/Keyboard/Mouse). Checking one disables input
    /// from that type of device while this game is running — e.g. Lightgun and
    /// Wheel for a fighting game, so they can't interfere. Nearly always all
    /// unchecked. Fixed list, not data-driven — unlike categories, these types
    /// don't change at runtime, so this is populated once in the constructor rather
    /// than synced from elsewhere.
    /// </summary>
    public ObservableCollection<DeviceTypeCheckItem> DisabledDeviceTypeOptions { get; } = [];

    /// <summary>
    /// Richer, per-player-slot alternative to DisabledDeviceTypeOptions above — see
    /// Game.PlayerDeviceAssignments. Nearly always empty; DisabledDeviceTypeOptions
    /// alone covers most games. Flat list rather than a nested Player→Devices
    /// structure so it can reuse the same enter-list/Up-Down/Confirm-remove/Back-exit
    /// convention as every other list in this editor — entries are grouped by
    /// PlayerIndex only at display/save time (see BuildPlayerDeviceAssignments()).
    /// </summary>
    public ObservableCollection<PlayerDeviceAssignmentEntry> PlayerDeviceAssignmentEntries { get; } = [];

    /// <summary>Rebuilds PlayerDeviceAssignmentEntries from the game's stored
    /// assignments, resolving each hardware path to a friendly name via the current
    /// peripheral registry (falls back to the raw path if the device is unknown/
    /// disconnected). Call on load, same convention as SyncCategoryOptions.</summary>
    public void SyncPlayerDeviceAssignments(IEnumerable<PlayerDeviceAssignment> assignments, IReadOnlyList<RawInputDevice> knownDevices)
    {
        PlayerDeviceAssignmentEntries.Clear();
        foreach (var slot in assignments.OrderBy(a => a.PlayerIndex))
        {
            foreach (var path in slot.PreferredHardwarePaths)
            {
                var name = knownDevices.FirstOrDefault(d =>
                    string.Equals(d.HardwarePath, path, StringComparison.OrdinalIgnoreCase))?.FriendlyName ?? path;
                PlayerDeviceAssignmentEntries.Add(new PlayerDeviceAssignmentEntry(slot.PlayerIndex, path, name));
            }
        }
    }

    /// <summary>Groups the flat entry list back into ranked per-player-slot
    /// assignments for persistence — entries sharing a PlayerIndex keep their
    /// existing relative order (first = most preferred).</summary>
    public List<PlayerDeviceAssignment> BuildPlayerDeviceAssignments() =>
        PlayerDeviceAssignmentEntries
            .GroupBy(e => e.PlayerIndex)
            .OrderBy(g => g.Key)
            .Select(g => new PlayerDeviceAssignment { PlayerIndex = g.Key, PreferredHardwarePaths = g.Select(e => e.HardwarePath).ToList() })
            .ToList();

    public GameEditViewModel()
    {
        foreach (var type in new[]
        {
            RawInputDeviceType.Gamepad,
            RawInputDeviceType.Lightgun,
            RawInputDeviceType.Wheel,
            RawInputDeviceType.Spinner,
            RawInputDeviceType.Trackball,
        })
        {
            DisabledDeviceTypeOptions.Add(new DeviceTypeCheckItem(type, type.ToString()));
        }
    }

    // Validation
    [ObservableProperty] private string _validationError = string.Empty;
    public bool IsValid => !string.IsNullOrWhiteSpace(Title) &&
                           !string.IsNullOrWhiteSpace(SelectedSystemId) &&
                           SelectedCategoryIds.Count > 0 &&
                           !string.IsNullOrWhiteSpace(SelectedEmulatorId);

    /// <summary>
    /// Rebuilds CategoryOptions from the current category catalog, preserving whichever
    /// are already checked. Call this whenever the category list changes (e.g. after
    /// InitializeAsync, or if a category is added/deleted while the editor is open).
    /// </summary>
    public void SyncCategoryOptions(IEnumerable<Category> categories)
    {
        var previouslyChecked = new HashSet<string>(
            CategoryOptions.Where(c => c.IsChecked).Select(c => c.Id),
            StringComparer.OrdinalIgnoreCase);

        CategoryOptions.Clear();
        foreach (var category in categories)
        {
            var item = new CategoryCheckItem(category.Id, category.Label)
            {
                IsChecked = previouslyChecked.Contains(category.Id)
            };
            CategoryOptions.Add(item);
        }
    }

    public void PopulateFrom(Game game)
    {
        Id = game.Id;
        Title = game.Title;
        SelectedSystemId = game.SystemId;
        SelectedEmulatorId = game.EmulatorId;
        RomPath = game.RomPath;
        ProcessNameOverride = game.ProcessNameOverride;
        Genre = game.Genre;
        Players = game.Players;
        IsFavorite = game.IsFavorite;
        CoverPath = game.Media.CoverPath;
        BackgroundPath = game.Media.BackgroundPath;
        LogoPath = game.Media.LogoPath;
        VideoPath = game.Media.VideoPath;
        MarqueePath = game.Media.MarqueePath;
        CardArtPath = game.Media.CardArtPath;
        PreferCardArtAsCover = game.Media.PreferCardArtAsCover;
        ScreenshotPaths.Clear();
        foreach (var screenshot in game.Media.ScreenshotPaths) ScreenshotPaths.Add(screenshot);
        BezelOverridePath = game.BezelOverridePath;
        DisplayModeOverrideWidth     = game.DisplayModeOverride?.Width     ?? 0;
        DisplayModeOverrideHeight    = game.DisplayModeOverride?.Height    ?? 0;
        DisplayModeOverrideRefreshHz = game.DisplayModeOverride?.RefreshHz ?? 0;
        DemulShooterTarget = game.DemulShooterTarget;
        BiosOverridePaths.Clear();
        foreach (var bios in game.BiosOverridePaths) BiosOverridePaths.Add(bios);

        var disabledTypes = new HashSet<RawInputDeviceType>(game.DisabledDeviceTypes);
        foreach (var option in DisabledDeviceTypeOptions)
            option.IsChecked = disabledTypes.Contains(option.Type);

        var checkedIds = new HashSet<string>(game.CategoryIds, StringComparer.OrdinalIgnoreCase);
        foreach (var option in CategoryOptions)
            option.IsChecked = checkedIds.Contains(option.Id);
    }

    public Game ToGame()
    {
        // Auto-generate Id from title if creating new
        var id = string.IsNullOrWhiteSpace(Id)
            ? Title.ToLowerInvariant()
                   .Replace(" ", "")
                   .Replace("'", "")
                   .Replace(".", "")
            : Id;

        return new Game
        {
            Id = id,
            Title = Title.Trim(),
            SystemId = SelectedSystemId,
            CategoryIds = SelectedCategoryIds.ToList(),
            EmulatorId = SelectedEmulatorId,
            // Stored relative to the app's own folder when possible, so this keeps
            // working if the whole portable install moves to a different drive
            // letter or machine — ProcessEmulatorLauncher.ResolveRomPath (for RomPath)
            // and MediaAssetResolver.ResolvePath (for the Media.* paths below) both
            // already resolve either form back to an absolute path when loading.
            RomPath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(RomPath.Trim()),
            ProcessNameOverride = ProcessNameOverride.Trim(),
            Genre = Genre.Trim(),
            Players = Players,
            IsFavorite = IsFavorite,
            Media = new GameMedia
            {
                CoverPath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(CoverPath.Trim()),
                BackgroundPath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(BackgroundPath.Trim()),
                LogoPath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(LogoPath.Trim()),
                VideoPath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(VideoPath.Trim()),
                MarqueePath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(MarqueePath.Trim()),
                CardArtPath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(CardArtPath.Trim()),
                PreferCardArtAsCover = PreferCardArtAsCover,
                ScreenshotPaths = ScreenshotPaths
                    .Select(p => UGL.Core.Utilities.PortablePathHelper.ToPortablePath(p))
                    .ToList(),
            },
            BezelOverridePath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(BezelOverridePath.Trim()),
            DisplayModeOverride = (DisplayModeOverrideWidth, DisplayModeOverrideHeight, DisplayModeOverrideRefreshHz) == (0, 0, 0)
                ? null
                : new DisplayMode
                {
                    Width     = DisplayModeOverrideWidth     == 0 ? null : DisplayModeOverrideWidth,
                    Height    = DisplayModeOverrideHeight    == 0 ? null : DisplayModeOverrideHeight,
                    RefreshHz = DisplayModeOverrideRefreshHz == 0 ? null : DisplayModeOverrideRefreshHz,
                },
            DemulShooterTarget = DemulShooterTarget.Trim(),
            BiosOverridePaths = BiosOverridePaths
                .Select(p => UGL.Core.Utilities.PortablePathHelper.ToPortablePath(p))
                .ToList(),
            DisabledDeviceTypes = DisabledDeviceTypeOptions
                .Where(o => o.IsChecked)
                .Select(o => o.Type)
                .ToList(),
            PlayerDeviceAssignments = BuildPlayerDeviceAssignments(),
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
            ValidationError = "Title is required.";
        else if (string.IsNullOrWhiteSpace(SelectedSystemId))
            ValidationError = "System is required.";
        else if (SelectedCategoryIds.Count == 0)
            ValidationError = "At least one category is required.";
        else if (string.IsNullOrWhiteSpace(SelectedEmulatorId))
            ValidationError = "Emulator is required.";
        else
            ValidationError = string.Empty;
    }
}
