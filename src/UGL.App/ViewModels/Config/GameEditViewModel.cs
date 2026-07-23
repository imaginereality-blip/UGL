using System.Collections.ObjectModel;
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

    /// <summary>Rare per-game override of the system's default bezel image. Empty in
    /// the overwhelming majority of cases.</summary>
    [ObservableProperty] private string _bezelOverridePath = string.Empty;

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
        Genre = game.Genre;
        Players = game.Players;
        IsFavorite = game.IsFavorite;
        CoverPath = game.Media.CoverPath;
        BackgroundPath = game.Media.BackgroundPath;
        LogoPath = game.Media.LogoPath;
        VideoPath = game.Media.VideoPath;
        MarqueePath = game.Media.MarqueePath;
        BezelOverridePath = game.BezelOverridePath;
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
            },
            BezelOverridePath = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(BezelOverridePath.Trim()),
            BiosOverridePaths = BiosOverridePaths
                .Select(p => UGL.Core.Utilities.PortablePathHelper.ToPortablePath(p))
                .ToList(),
            DisabledDeviceTypes = DisabledDeviceTypeOptions
                .Where(o => o.IsChecked)
                .Select(o => o.Type)
                .ToList(),
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
