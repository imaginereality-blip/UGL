using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

public sealed partial class CategoriesConfigViewModel : ObservableObject
{
    private readonly IConfigurationService _config;
    private readonly UGL.Media.SkiaMediaCache _mediaCache;
    private readonly VirtualKeyboardViewModel _virtualKeyboard;
    private readonly ILogger<CategoriesConfigViewModel> _logger;

    public ObservableCollection<Category> Categories { get; } = [];

    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private string _categoryId = string.Empty;
    [ObservableProperty] private string _categoryLabel = string.Empty;
    [ObservableProperty] private int _categoryOrder;
    [ObservableProperty] private string _categoryArtPath = string.Empty;
    [ObservableProperty] private string _categoryBackgroundPath = string.Empty;
    [ObservableProperty] private string _categoryVideoPath = string.Empty;
    [ObservableProperty] private string _categoryAccentColor = string.Empty;
    [ObservableProperty] private string _categoryDescription = string.Empty;
    [ObservableProperty] private string _categoryIconKey = string.Empty;
    [ObservableProperty] private string _categoryIconPreviewGlyph = string.Empty;
    [ObservableProperty] private string _categoryIconPreviewMessage = string.Empty;
    [ObservableProperty] private string _categoryStatusMessage = string.Empty;

    public event Func<string, string[], Task<string?>>? BrowseFileRequested;

    public CategoriesConfigViewModel(
        IConfigurationService config,
        UGL.Media.SkiaMediaCache mediaCache,
        VirtualKeyboardViewModel virtualKeyboard,
        ILogger<CategoriesConfigViewModel> logger)
    {
        _config = config;
        _mediaCache = mediaCache;
        _virtualKeyboard = virtualKeyboard;
        _logger = logger;

        Categories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(UsedOrdersHint));
        CardDimensionInfo.Changed += () => OnPropertyChanged(nameof(RecommendedArtSizeHint));
    }

    /// <summary>
    /// The actual on-screen size of a category card, in real device pixels, measured
    /// live from a rendered CategoryCard. Empty until the Home Menu has rendered at
    /// least once (it always has by the time Settings can be opened, since the app
    /// starts on the Home Menu — but shown defensively regardless).
    /// </summary>
    public string RecommendedArtSizeHint
    {
        get
        {
            var size = CardDimensionInfo.CategoryCardPixelSize;
            return size.Width > 0 && size.Height > 0
                ? $"Recommended art size: {size.Width:F0}×{size.Height:F0}px (based on your current window)"
                : "Recommended size unknown yet";
        }
    }

    /// <summary>
    /// Reserved category Id — same reserved Id JsonGameRepository/HomeMenuViewModel
    /// treat specially. Unlike a normal category, its game membership is computed from
    /// Game.IsFavorite rather than manual assignment, so it must never be renamed or
    /// deleted — see IsSelectedCategoryProtected below.
    /// </summary>
    private const string FavoritesCategoryId = "favorites";

    public async Task InitializeAsync()
    {
        var categories = (await _config.GetCategoriesAsync()).ToList();

        // Auto-seed once, so the user can customize its art/description immediately
        // without any manual setup — it isn't something they create themselves.
        if (!categories.Any(c => string.Equals(c.Id, FavoritesCategoryId, StringComparison.OrdinalIgnoreCase)))
        {
            var seeded = new Category
            {
                Id = FavoritesCategoryId,
                Label = "Favorites",
                Order = -1,
                IconKey = string.Empty,
                ArtPath = string.Empty,
                BackgroundPath = string.Empty,
                AccentColor = string.Empty,
                Description = "Games you've marked as favorite.",
            };
            categories.Add(seeded);
            await _config.UpdateCategoriesAsync(categories);
        }

        Categories.Clear();
        foreach (var c in categories) Categories.Add(c);
        SelectedCategory = Categories.FirstOrDefault();
    }

    /// <summary>
    /// True when the currently selected category is the reserved Favorites entry —
    /// gates both Delete (disabled entirely) and the Id field (locked, since renaming
    /// it would silently break the special-case matching everywhere else that expects
    /// exactly "favorites").
    /// </summary>
    public bool IsSelectedCategoryProtected =>
        SelectedCategory is not null &&
        string.Equals(SelectedCategory.Id, FavoritesCategoryId, StringComparison.OrdinalIgnoreCase);

    public bool CanDeleteSelectedCategory => SelectedCategory is not null && !IsSelectedCategoryProtected;

    partial void OnSelectedCategoryChanged(Category? value)
    {
        PopulateCategoryEditor(value);
        OnPropertyChanged(nameof(UsedOrdersHint));
        OnPropertyChanged(nameof(IsSelectedCategoryProtected));
        OnPropertyChanged(nameof(CanDeleteSelectedCategory));
    }

    /// <summary>
    /// Shows which Order numbers other categories already use, so picking a number
    /// for this one is informed rather than guesswork. The currently-selected category's
    /// own order is excluded, since re-saving it with the same number isn't a conflict.
    /// </summary>
    public string UsedOrdersHint
    {
        get
        {
            var used = Categories
                .Where(c => !ReferenceEquals(c, SelectedCategory))
                .Select(c => c.Order)
                .Distinct()
                .OrderBy(o => o)
                .ToList();

            return used.Count == 0
                ? "No other categories yet."
                : $"Already in use: {string.Join(", ", used)}";
        }
    }

    // ── Field highlight ───────────────────────────────────────────────────
    [ObservableProperty] private bool _isCategoryListFocused = true;
    [ObservableProperty] private int _categoryFocusIndex;
    private const int CategoryFieldCount = 11; // Id,Label,Order,Accent,Art,Background,IconKey,Description,Add,Save,Delete

    public bool IsCategoryIdFocused          => !IsCategoryListFocused && CategoryFocusIndex == 0;
    public bool IsCategoryLabelFocused       => !IsCategoryListFocused && CategoryFocusIndex == 1;
    public bool IsCategoryOrderFocused       => !IsCategoryListFocused && CategoryFocusIndex == 2;
    public bool IsCategoryAccentFocused      => !IsCategoryListFocused && CategoryFocusIndex == 3;
    public bool IsCategoryArtFocused         => !IsCategoryListFocused && CategoryFocusIndex == 4;
    public bool IsCategoryBackgroundFocused  => !IsCategoryListFocused && CategoryFocusIndex == 5;
    public bool IsCategoryIconKeyFocused     => !IsCategoryListFocused && CategoryFocusIndex == 6;
    public bool IsCategoryDescriptionFocused => !IsCategoryListFocused && CategoryFocusIndex == 7;
    public bool IsCategoryAddFocused         => !IsCategoryListFocused && CategoryFocusIndex == 8;
    public bool IsCategorySaveFocused        => !IsCategoryListFocused && CategoryFocusIndex == 9;
    public bool IsCategoryDeleteFocused      => !IsCategoryListFocused && CategoryFocusIndex == 10;

    partial void OnCategoryFocusIndexChanged(int value) => RaiseAllCategoryFieldChanged();
    partial void OnIsCategoryListFocusedChanged(bool value) => RaiseAllCategoryFieldChanged();

    private void RaiseAllCategoryFieldChanged()
    {
        OnPropertyChanged(nameof(IsCategoryIdFocused));
        OnPropertyChanged(nameof(IsCategoryLabelFocused));
        OnPropertyChanged(nameof(IsCategoryOrderFocused));
        OnPropertyChanged(nameof(IsCategoryAccentFocused));
        OnPropertyChanged(nameof(IsCategoryArtFocused));
        OnPropertyChanged(nameof(IsCategoryBackgroundFocused));
        OnPropertyChanged(nameof(IsCategoryIconKeyFocused));
        OnPropertyChanged(nameof(IsCategoryDescriptionFocused));
        OnPropertyChanged(nameof(IsCategoryAddFocused));
        OnPropertyChanged(nameof(IsCategorySaveFocused));
        OnPropertyChanged(nameof(IsCategoryDeleteFocused));
    }

    // ── Controller navigation ────────────────────────────────────────────
    // Now a standalone tab (no more Themes section to switch to), so Left/Right is
    // reserved solely for the Order field; A/Select enters the field editor from the
    // list, Up from the first field exits back to the list.
    public void NavigateUp()
    {
        if (IsCategoryListFocused) { MoveCategorySelection(-1); return; }
        if (CategoryFocusIndex == 0) { IsCategoryListFocused = true; return; }
        CategoryFocusIndex = (CategoryFocusIndex - 1 + CategoryFieldCount) % CategoryFieldCount;
    }

    public void NavigateDown()
    {
        if (IsCategoryListFocused) { MoveCategorySelection(1); return; }
        CategoryFocusIndex = (CategoryFocusIndex + 1) % CategoryFieldCount;
    }

    public void NavigateLeft()
    {
        if (!IsCategoryListFocused && CategoryFocusIndex == 2)
            CategoryOrder = Math.Max(0, CategoryOrder - 1);
    }

    public void NavigateRight()
    {
        if (!IsCategoryListFocused && CategoryFocusIndex == 2)
            CategoryOrder = Math.Min(100, CategoryOrder + 1);
    }

    public async Task ConfirmAsync()
    {
        if (IsCategoryListFocused)
        {
            IsCategoryListFocused = false; // enter the category editor fields
            CategoryFocusIndex = 0;
            return;
        }

        switch (CategoryFocusIndex)
        {
            case 0:
                if (IsSelectedCategoryProtected)
                    CategoryStatusMessage = "The Favorites category's Id can't be changed.";
                else
                    _virtualKeyboard.Open("Category ID", CategoryId, v => CategoryId = v);
                break;
            case 1: _virtualKeyboard.Open("Category Label", CategoryLabel, v => CategoryLabel = v); break;
            case 2: break; // Order — adjusted via Left/Right, not Confirm
            case 3: _virtualKeyboard.Open("Accent Color", CategoryAccentColor, v => CategoryAccentColor = v); break;
            case 4: await BrowseCategoryImageAsync(); break;
            case 5: await BrowseCategoryBackgroundAsync(); break;
            case 6: _virtualKeyboard.Open("Icon Key", CategoryIconKey, v => CategoryIconKey = v); break;
            case 7: _virtualKeyboard.Open("Description", CategoryDescription, v => CategoryDescription = v); break;
            case 8: AddCategory(); break;
            case 9: await SaveCategoryAsync(); break;
            case 10: await DeleteCategoryAsync(); break;
        }
    }

    private void MoveCategorySelection(int delta)
    {
        if (Categories.Count == 0) return;
        int idx = SelectedCategory is null ? 0 : Categories.IndexOf(SelectedCategory);
        idx = (idx + delta + Categories.Count) % Categories.Count;
        SelectedCategory = Categories[idx];
    }

    private static readonly Dictionary<string, string> IconPreviewEmojiMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["icon_fighting"] = "⚔️",
        ["icon_racing"] = "🏁",
        ["icon_fps"]      = "🎯",
        ["icon_sports"]   = "🏆",
        ["icon_platform"] = "🪂",
        ["icon_puzzle"]   = "🧩",
        ["icon_arcade"]   = "🕹️",
        ["icon_pinball"]  = "🎳",
    };

    partial void OnCategoryIconKeyChanged(string value)
        => UpdateIconPreview(value);

    private void PopulateCategoryEditor(Category? category)
    {
        if (category is null)
        {
            CategoryId = string.Empty;
            CategoryLabel = string.Empty;
            CategoryOrder = 0;
            CategoryArtPath = string.Empty;
            CategoryBackgroundPath = string.Empty;
            CategoryAccentColor = string.Empty;
            CategoryDescription = string.Empty;
            CategoryIconKey = string.Empty;
            return;
        }

        CategoryId = category.Id;
        CategoryLabel = category.Label;
        CategoryOrder = category.Order;
        CategoryArtPath = category.ArtPath;
        CategoryBackgroundPath = category.BackgroundPath;
        CategoryAccentColor = category.AccentColor;
        CategoryDescription = category.Description;
        CategoryIconKey = category.IconKey;
    }

    private void UpdateIconPreview(string iconKey)
    {
        var trimmedKey = iconKey?.Trim();
        if (string.IsNullOrEmpty(trimmedKey))
        {
            CategoryIconPreviewGlyph = "⌛";
            CategoryIconPreviewMessage = "No icon selected.";
            return;
        }

        if (IconPreviewEmojiMap.TryGetValue(trimmedKey, out var glyph))
        {
            CategoryIconPreviewGlyph = glyph;
            CategoryIconPreviewMessage = $"Preview for '{trimmedKey}'.";
            return;
        }

        CategoryIconPreviewGlyph = "❓";
        CategoryIconPreviewMessage = $"No preview available for '{trimmedKey}'.";
    }

    [RelayCommand]
    private async Task SaveCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(CategoryId) || string.IsNullOrWhiteSpace(CategoryLabel))
        {
            CategoryStatusMessage = "Category ID and label are required.";
            return;
        }

        // Copy art/background into media/categories/ so the watcher
        // always points inside the UGL folder, and paths are portable.
        var artPath   = await CopyToCategoriesMediaAsync(CategoryArtPath.Trim());
        var bgPath    = await CopyToCategoriesMediaAsync(CategoryBackgroundPath.Trim());
        var videoPath = await CopyToCategoriesMediaAsync(CategoryVideoPath.Trim());

        var updatedCategory = new Category
        {
            Id             = CategoryId.Trim(),
            Label          = CategoryLabel.Trim(),
            Order          = CategoryOrder,
            ArtPath        = artPath        ?? UGL.Core.Utilities.PortablePathHelper.ToPortablePath(CategoryArtPath.Trim()),
            BackgroundPath = bgPath         ?? UGL.Core.Utilities.PortablePathHelper.ToPortablePath(CategoryBackgroundPath.Trim()),
            VideoPath      = videoPath      ?? UGL.Core.Utilities.PortablePathHelper.ToPortablePath(CategoryVideoPath.Trim()),
            AccentColor    = CategoryAccentColor.Trim(),
            Description    = CategoryDescription.Trim(),
            IconKey        = CategoryIconKey.Trim(),
        };

        var existingIndex = SelectedCategory is not null
            ? Categories.IndexOf(Categories.FirstOrDefault(c => string.Equals(c.Id, SelectedCategory.Id, StringComparison.OrdinalIgnoreCase))!)
            : -1;

        if (existingIndex >= 0)
            Categories[existingIndex] = updatedCategory;
        else
        {
            if (Categories.Any(c => string.Equals(c.Id, updatedCategory.Id, StringComparison.OrdinalIgnoreCase)))
            {
                CategoryStatusMessage = "A category with that ID already exists.";
                return;
            }
            Categories.Add(updatedCategory);
        }

        // Update bound fields to reflect copied paths
        CategoryArtPath        = updatedCategory.ArtPath;
        CategoryBackgroundPath = updatedCategory.BackgroundPath;
        CategoryVideoPath      = updatedCategory.VideoPath;

        SelectedCategory = updatedCategory;
        await _config.UpdateCategoriesAsync(Categories);
        CategoryStatusMessage = $"Category '{updatedCategory.Label}' saved.";
        _logger.LogInformation("Category saved: {CategoryId}", updatedCategory.Id);

        // Evict old cached bitmaps and notify home menu to reload cards immediately.
        // This triggers the same live-reload path as FileSystemWatcher so the card
        // updates as soon as Settings is closed — no restart required.
        if (!string.IsNullOrWhiteSpace(updatedCategory.ArtPath))
        {
            _mediaCache.EvictImage(updatedCategory.ArtPath);
            _mediaCache.RaiseImageChanged(updatedCategory.ArtPath);
        }
        if (!string.IsNullOrWhiteSpace(updatedCategory.BackgroundPath))
        {
            _mediaCache.EvictImage(updatedCategory.BackgroundPath);
            _mediaCache.RaiseImageChanged(updatedCategory.BackgroundPath);
        }
    }

    /// <summary>
    /// Copies a file into {MediaRootPath}/categories/ preserving the original filename.
    /// Returns the destination absolute path, or null if sourcePath is empty/missing.
    /// If the file is already inside the media folder, returns it unchanged.
    /// </summary>
    private async Task<string?> CopyToCategoriesMediaAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        var absSource = Path.IsPathRooted(sourcePath)
            ? sourcePath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, sourcePath));

        if (!File.Exists(absSource)) return null;

        var mediaRoot  = Path.IsPathRooted(_config.Settings.MediaRootPath)
            ? _config.Settings.MediaRootPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _config.Settings.MediaRootPath));

        var destDir  = Path.Combine(mediaRoot, "categories");
        var destPath = Path.Combine(destDir, Path.GetFileName(absSource));

        // Already in the right place
        if (absSource.Equals(destPath, StringComparison.OrdinalIgnoreCase))
            return UGL.Core.Utilities.PortablePathHelper.ToPortablePath(destPath);

        Directory.CreateDirectory(destDir);
        await Task.Run(() => File.Copy(absSource, destPath, overwrite: true));
        _logger.LogInformation("Copied category media: {Src} → {Dst}", absSource, destPath);

        // Stored relative to the app's own folder when possible, so this keeps
        // working if the whole portable install moves to a different drive letter
        // or machine — MediaAssetResolver.ResolvePath already resolves either form
        // back to an absolute path when loading.
        return UGL.Core.Utilities.PortablePathHelper.ToPortablePath(destPath);
    }

    [RelayCommand]
    private void AddCategory()
    {
        var newCategory = new Category
        {
            Id = string.Empty,
            Label = string.Empty,
            Order = Categories.Count,
            ArtPath = string.Empty,
            BackgroundPath = string.Empty,
            AccentColor = string.Empty,
            Description = string.Empty,
            IconKey = string.Empty,
        };
        // Must actually be a member of Categories before assigning it as SelectedCategory —
        // the ListBox's two-way-bound SelectedItem silently rejects (resets to null) any
        // value that isn't part of its ItemsSource, which left Save permanently disabled.
        Categories.Add(newCategory);
        SelectedCategory = newCategory;
        CategoryStatusMessage = "Enter the new category details and click Save.";
    }

    /// <summary>
    /// Direct "add new" shortcut (bound to X while browsing the category list) —
    /// adds a blank category and immediately enters field-edit mode on it, landing on
    /// the Id field. Without this, the only controller path to a new category was
    /// Confirm on an *existing* one (entering its fields), then navigating down eight
    /// positions to reach the Add button — a real, reported usability problem, not
    /// just an inconvenience, since it made "start a new category" feel gated behind
    /// "first select an existing one."
    /// </summary>
    public void QuickAddNew()
    {
        AddCategory();
        IsCategoryListFocused = false;
        CategoryFocusIndex = 0;
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategory is null) return;

        // Guard here, not just via the Delete button's IsEnabled — controller-driven
        // Confirm calls this method directly and doesn't go through Avalonia's real
        // enabled/interaction state, so a visual-only disable wouldn't actually stop it.
        if (IsSelectedCategoryProtected)
        {
            CategoryStatusMessage = "Favorites can't be deleted.";
            return;
        }

        Categories.Remove(SelectedCategory);
        await _config.UpdateCategoriesAsync(Categories);
        SelectedCategory = Categories.FirstOrDefault();
        CategoryStatusMessage = "Category deleted.";
        _logger.LogInformation("Category deleted: {CategoryId}", SelectedCategory?.Id ?? "(none)");
    }

    [RelayCommand]
    private async Task BrowseCategoryImageAsync()
        => await BrowseCategoryFileAsync("Category Image", new[] { "*.jpg", "*.jpeg", "*.png", "*.webp" }, isArt: true);

    [RelayCommand]
    private async Task BrowseCategoryBackgroundAsync()
        => await BrowseCategoryFileAsync("Category Background", new[] { "*.jpg", "*.jpeg", "*.png", "*.webp" }, isArt: false);

    private async Task BrowseCategoryFileAsync(string title, string[] patterns, bool isArt)
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke(title, patterns);
        if (path is null) return;

        if (isArt)
            CategoryArtPath = path;
        else
            CategoryBackgroundPath = path;

        // Autosave: if a category is selected and has ID and Label, persist
        // the change immediately so the home menu can reflect it without
        // requiring the user to click Save.
        if (SelectedCategory is not null &&
            !string.IsNullOrWhiteSpace(CategoryId) &&
            !string.IsNullOrWhiteSpace(CategoryLabel))
        {
            await SaveCategoryAsync();
        }
    }
}
