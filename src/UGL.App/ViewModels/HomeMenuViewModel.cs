using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;
using UGL.Media;

namespace UGL.App.ViewModels;

public sealed partial class HomeMenuViewModel : ObservableObject
{
    private readonly IConfigurationService _configuration;
    private readonly IGameRepository _gameRepository;
    private readonly SkiaMediaCache _mediaCache;
    private readonly MediaAssetResolver _resolver;
    private readonly ILogger<HomeMenuViewModel> _logger;

    /// <summary>
    /// Reserved category Id — Favorites is never user-created or stored in
    /// categories.json. It's synthesized here, prepended as the first card, only when
    /// at least one game is currently favorited, and simply omitted otherwise. See
    /// BuildCategoryListAsync.
    /// </summary>
    private const string FavoritesCategoryId = "favorites";

    private const int VisibleCardCount = 5;

    private List<Category> _allCategories = [];
    private int _categoryIndex;
    private CancellationTokenSource _navCts = new();
    private CancellationTokenSource _reloadCts = new();

    // Fixed pool of 5 VM instances — never replaced, only updated in-place
    private readonly List<CategoryCardViewModel> _vmPool = [];

    public ObservableCollection<CategoryCardViewModel> VisibleCards { get; } = [];

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _selectedCategoryLabel = string.Empty;

    public event Func<Category, Task>? CategoryConfirmed;

    public HomeMenuViewModel(
        IConfigurationService configuration,
        IGameRepository gameRepository,
        SkiaMediaCache mediaCache,
        MediaAssetResolver resolver,
        ILogger<HomeMenuViewModel> logger)
    {
        _configuration = configuration;
        _gameRepository = gameRepository;
        _mediaCache = mediaCache;
        _resolver = resolver;
        _logger = logger;
        _mediaCache.ImageChanged += OnImageChanged;
    }

    /// <summary>
    /// Loads the user-defined category list and prepends a synthetic Favorites entry
    /// as the first card if — and only if — at least one game is currently favorited.
    /// Not persisted anywhere and never shown in the Categories settings tab, since it
    /// isn't a real row in categories.json; membership is entirely computed from
    /// Game.IsFavorite (JsonGameRepository.GetGamesByCategoryAsync).
    /// </summary>
    private async Task<List<Category>> BuildCategoryListAsync(CancellationToken ct)
    {
        var categories = (await _configuration.GetCategoriesAsync(ct)).OrderBy(c => c.Order).ToList();

        // Favorites is now a real, persisted row (CategoriesConfigViewModel auto-seeds
        // it once so its art/description can be customized in Settings), but it's only
        // ever *shown* here — as the first card — when at least one game is currently
        // favorited. Pull it out of wherever it naturally sorted to and reinsert at the
        // front, rather than leaving it in its Order-based position.
        var favoritesCategory = categories.FirstOrDefault(
            c => string.Equals(c.Id, FavoritesCategoryId, StringComparison.OrdinalIgnoreCase));
        if (favoritesCategory is not null)
            categories.Remove(favoritesCategory);

        var allGames = await _gameRepository.GetAllGamesAsync(ct);
        bool hasFavorites = allGames.Any(g => g.IsFavorite);

        if (hasFavorites)
        {
            // Defensive fallback in case InitializeAsync somehow ran before the
            // Categories tab ever auto-seeded a stored row — shouldn't normally happen,
            // but browsing still works with default art rather than failing outright.
            categories.Insert(0, favoritesCategory ?? new Category
            {
                Id = FavoritesCategoryId,
                Label = "Favorites",
                Order = -1,
                IconKey = string.Empty,
                ArtPath = string.Empty,
                BackgroundPath = string.Empty,
                AccentColor = string.Empty,
                Description = "Games you've marked as favorite.",
            });
        }

        return categories;
    }

    private void OnImageChanged(string path)
    {
        _logger.LogInformation("[HomeMenu] ImageChanged received for: {Path}", path);
        Dispatcher.UIThread.Post(() =>
        {
            _logger.LogInformation("[HomeMenu] Dispatched to UI thread, triggering reload");
            _reloadCts.Cancel();
            _reloadCts.Dispose();
            _reloadCts = new CancellationTokenSource();
            _ = LoadVisibleCoversAsync(_reloadCts.Token);
        }, DispatcherPriority.Background);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            _allCategories = await BuildCategoryListAsync(ct);

            var defaultId = _configuration.Settings.DefaultCategoryId;
            _categoryIndex = _allCategories.FindIndex(
                c => string.Equals(c.Id, defaultId, StringComparison.OrdinalIgnoreCase));
            if (_categoryIndex < 0) _categoryIndex = 0;

            if (_allCategories.Count == 0) { _logger.LogWarning("No categories loaded."); return; }

            RefreshVisibleCards();
            _ = LoadVisibleCoversAsync(_navCts.Token);
        }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// Called when returning to the Home Menu from the Game Browser. Re-checks
    /// whether Favorites should appear/disappear (the user may have just
    /// favorited/unfavorited a game there via X), in addition to the existing
    /// card/cover refresh.
    /// </summary>
    public async Task RestoreSelection()
    {
        var previouslySelectedId = _allCategories.Count > 0 && _categoryIndex < _allCategories.Count
            ? _allCategories[_categoryIndex].Id
            : null;

        _allCategories = await BuildCategoryListAsync(_navCts.Token);

        _categoryIndex = previouslySelectedId is null
            ? 0
            : Math.Max(0, _allCategories.FindIndex(c => c.Id == previouslySelectedId));

        RefreshVisibleCards();
        _ = LoadVisibleCoversAsync(_navCts.Token);
    }

    public Task NavigateRightAsync()
    {
        if (_allCategories.Count == 0) return Task.CompletedTask;
        _categoryIndex = (_categoryIndex + 1) % _allCategories.Count;
        RefreshVisibleCards();
        _ = LoadVisibleCoversAsync(CancelAndRenewNavToken());
        return Task.CompletedTask;
    }

    public Task NavigateLeftAsync()
    {
        if (_allCategories.Count == 0) return Task.CompletedTask;
        _categoryIndex = (_categoryIndex - 1 + _allCategories.Count) % _allCategories.Count;
        RefreshVisibleCards();
        _ = LoadVisibleCoversAsync(CancelAndRenewNavToken());
        return Task.CompletedTask;
    }

    private CancellationToken CancelAndRenewNavToken()
    {
        _navCts.Cancel();
        _navCts.Dispose();
        _navCts = new CancellationTokenSource();
        return _navCts.Token;
    }

    public async Task ConfirmAsync()
    {
        if (_allCategories.Count == 0) return;
        var category = _allCategories[_categoryIndex];
        if (CategoryConfirmed is not null)
            await CategoryConfirmed.Invoke(category);
    }

    private async Task LoadVisibleCoversAsync(CancellationToken ct)
    {
        try
        {
            var tasks = VisibleCards.Select(card => card.LoadCoverAsync(_mediaCache, _resolver, ct));
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Reloads category data from config (picks up ArtPath changes saved in Settings)
    /// and reloads covers for visible cards without rebuilding the VM pool.
    /// Called by MainWindowViewModel when Settings closes.
    /// </summary>
    public async Task RefreshCategoriesAsync()
    {
        var previouslySelectedId = _allCategories.Count > 0 && _categoryIndex < _allCategories.Count
            ? _allCategories[_categoryIndex].Id
            : null;

        // Patch ArtPath on already-known categories before rebuilding — RefreshVisibleCards
        // below only replaces a pool VM's Category when the Id itself changes, so an
        // ArtPath edit on a category that's still the same Id needs this separate pass
        // to actually pick up the new art.
        var updatedCategories = await BuildCategoryListAsync(default);
        for (int i = 0; i < _vmPool.Count; i++)
        {
            var vm = _vmPool[i];
            var updated = updatedCategories.FirstOrDefault(c => c.Id == vm.Category.Id);
            if (updated is not null && updated.ArtPath != vm.Category.ArtPath)
                vm.Category = updated; // triggers OnCategoryChanged → resets CoverBitmap
        }

        _allCategories = updatedCategories;

        // RefreshVisibleCards handles any structural change — Favorites newly appearing
        // or disappearing, or a category added/removed in Settings — not just art edits.
        _categoryIndex = previouslySelectedId is null
            ? 0
            : Math.Max(0, _allCategories.FindIndex(c => c.Id == previouslySelectedId));
        RefreshVisibleCards();

        // Reload covers for all visible cards with fresh paths
        _reloadCts.Cancel();
        _reloadCts.Dispose();
        _reloadCts = new CancellationTokenSource();
        await LoadVisibleCoversAsync(_reloadCts.Token);
    }

    private void RefreshVisibleCards()
    {
        if (_allCategories.Count == 0)
        {
            VisibleCards.Clear();
            _vmPool.Clear();
            SelectedCategoryLabel = string.Empty;
            return;
        }

        int total = _allCategories.Count;

        // Never show fewer unique categories than exist, and never show the same
        // physical category twice in the row — cap the window to the catalog size
        // when it's smaller than VisibleCardCount.
        int slotCount = Math.Min(VisibleCardCount, total);
        int half = slotCount / 2;

        // Grow pool — new VMs for new slots only
        while (_vmPool.Count < slotCount)
            _vmPool.Add(new CategoryCardViewModel(_allCategories[0]));

        // Shrink pool
        while (_vmPool.Count > slotCount)
            _vmPool.RemoveAt(_vmPool.Count - 1);

        // Update each VM IN-PLACE by Id comparison — never replace the instance.
        // Circular windowing: slot offsets are centred on the selected category and
        // wrap around both ends via modulo, so the category after the last one in
        // the list is the first one again (and vice versa) — matches continuous
        // carousel browsing, same fix already applied to GameBrowserViewModel.
        for (int slot = 0; slot < slotCount; slot++)
        {
            int offset   = slot - half;
            int catIdx   = ((_categoryIndex + offset) % total + total) % total;
            var category = _allCategories[catIdx];
            var vm        = _vmPool[slot];
            bool selected = catIdx == _categoryIndex;

            // Update category by Id — this resets CoverBitmap via OnCategoryChanged
            if (vm.Category.Id != category.Id)
                vm.Category = category;

            vm.IsSelected = selected;
        }

        // Sync VisibleCards to pool — never replace existing entries
        for (int i = 0; i < slotCount; i++)
        {
            if (i < VisibleCards.Count)
            {
                if (!ReferenceEquals(VisibleCards[i], _vmPool[i]))
                    VisibleCards[i] = _vmPool[i];
            }
            else
            {
                VisibleCards.Add(_vmPool[i]);
            }
        }

        while (VisibleCards.Count > slotCount)
            VisibleCards.RemoveAt(VisibleCards.Count - 1);

        SelectedCategoryLabel = _allCategories[_categoryIndex].Label;
    }
}
