using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;
using UGL.Media;

namespace UGL.App.ViewModels;

/// <summary>
/// Level 2: Game Browser. Shows up to five full-height game cards for a
/// single category, centred on the focused game. B/Escape returns to Home.
///
/// This is the original DashboardViewModel's game-windowing logic
/// (RefreshVisibleCards over _allCardsForCategory), extracted and scoped
/// to one fixed category instead of switching categories internally.
/// </summary>
public sealed partial class GameBrowserViewModel : ObservableObject
{
    private readonly IConfigurationService _configuration;
    private readonly IGameRepository _gameRepository;
    private readonly SkiaMediaCache _mediaCache;
    private readonly MediaAssetResolver _resolver;
    private readonly IEmulatorLauncher _launcher;
    private readonly IVideoPreviewService _videoPreview;
    private readonly ILogger<GameBrowserViewModel> _logger;

    private const int VisibleCardCount = 5;

    private IReadOnlyDictionary<string, string> _systemNames = new Dictionary<string, string>();
    private List<GameCardViewModel> _allCardsForCategory = [];
    private int _gameIndex;

    /// <summary>Cancelled and replaced on every navigation action to abort stale loads.</summary>
    private CancellationTokenSource _navCts = new();

    /// <summary>The category this browser is scoped to. Set via SetCategoryAsync.</summary>
    public Category? ActiveCategory { get; private set; }

    /// <summary>The currently active filter. Updated by FilterOverlayViewModel via MainWindowViewModel.</summary>
    public GameFilter ActiveFilter { get; private set; } = GameFilter.Empty;

    public ObservableCollection<GameCardViewModel> VisibleCards { get; } = [];

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _selectedGameTitle = string.Empty;

    [ObservableProperty]
    private string _selectedGameSystem = string.Empty;

    [ObservableProperty]
    private string _selectedGamePlayers = string.Empty;

    [ObservableProperty]
    private bool _hasNoGames;

    /// <summary>Raised when B/Escape is pressed to return to the Home Menu.</summary>
    /// <summary>
    /// Exposed to GameCard.axaml.cs so it can call Play/Stop without
    /// requiring DI injection into the View layer directly.
    /// </summary>
    public IVideoPreviewService VideoPreview => _videoPreview;

    public event Action? BackRequested;

    /// <summary>Raised when A/Enter confirms a game. Launch logic lands in Milestone 9.</summary>
    public event Action<Game>? GameConfirmed;

    public GameBrowserViewModel(
        IConfigurationService configuration,
        IGameRepository gameRepository,
        SkiaMediaCache mediaCache,
        MediaAssetResolver resolver,
        IEmulatorLauncher launcher,
        IVideoPreviewService videoPreview,
        ILogger<GameBrowserViewModel> logger)
    {
        _configuration = configuration;
        _gameRepository = gameRepository;
        _mediaCache = mediaCache;
        _resolver = resolver;
        _launcher = launcher;
        _videoPreview = videoPreview;
        _logger = logger;

        // Live-reload: re-fetch visible covers when any image file changes on disk
        _mediaCache.ImageChanged += OnImageChanged;
    }

    private void OnImageChanged(string path)
    {
        _ = LoadVisibleCoversAsync(CancelAndRenewNavToken());
    }

    /// <summary>Loads games for the given category. Call each time Home Menu confirms a new category.</summary>
    public async Task SetCategoryAsync(Category category, GameFilter? filter = null, CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            ActiveCategory = category;
            ActiveFilter = filter ?? GameFilter.Empty;

            if (_systemNames.Count == 0)
            {
                var systems = await _configuration.GetSystemsAsync(ct);
                _systemNames = systems.ToDictionary(
                    s => s.Id,
                    s => s.Name,
                    StringComparer.OrdinalIgnoreCase);
            }

            var games = await _gameRepository.GetGamesByCategoryAsync(category.Id, ct);
            var filteredGames = ActiveFilter.Apply(games);

            _allCardsForCategory = filteredGames
                .Select(g => new GameCardViewModel(g, ResolveSystemName(g.SystemId)))
                .ToList();

            _gameIndex = 0;
            RefreshVisibleCards();
            _ = LoadVisibleCoversAsync(_navCts.Token);

            _logger.LogDebug(
                "Game Browser loaded category {Category} ({Count} games, filter active: {FilterActive})",
                category.Label, _allCardsForCategory.Count, !ActiveFilter.IsEmpty);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Navigation (called by MainWindowViewModel's input dispatch) ───────

    public Task NavigateRightAsync()
    {
        if (_allCardsForCategory.Count == 0) return Task.CompletedTask;
        _gameIndex = (_gameIndex + 1) % _allCardsForCategory.Count;
        RefreshVisibleCards();
        _ = LoadVisibleCoversAsync(CancelAndRenewNavToken());
        return Task.CompletedTask;
    }

    public Task NavigateLeftAsync()
    {
        if (_allCardsForCategory.Count == 0) return Task.CompletedTask;
        _gameIndex = (_gameIndex - 1 + _allCardsForCategory.Count) % _allCardsForCategory.Count;
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

    public Task ConfirmAsync()
    {
        if (_allCardsForCategory.Count == 0) return Task.CompletedTask;
        GameConfirmed?.Invoke(_allCardsForCategory[_gameIndex].Game);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Toggles favorite on the currently focused game and persists it immediately.
    /// Games join/leave the reserved "favorites" category automatically based on this
    /// flag (JsonGameRepository.GetGamesByCategoryAsync), so no separate category
    /// membership bookkeeping is needed here. If we're currently browsing Favorites
    /// itself and just unfavorited the focused game, the list is refreshed so it
    /// doesn't keep showing a card for a game that's no longer in view.
    /// </summary>
    public async Task ToggleFavoriteOnSelectedAsync()
    {
        if (_allCardsForCategory.Count == 0) return;
        var card = _allCardsForCategory[_gameIndex];

        card.Game.IsFavorite = !card.Game.IsFavorite;
        await _gameRepository.AddOrUpdateAsync(card.Game);
        await _gameRepository.SaveAsync();

        bool viewingFavorites = ActiveCategory is not null &&
            string.Equals(ActiveCategory.Id, "favorites", StringComparison.OrdinalIgnoreCase);

        if (viewingFavorites && !card.Game.IsFavorite)
            await SetCategoryAsync(ActiveCategory!, ActiveFilter, CancelAndRenewNavToken());
        else
            card.NotifyFavoriteChanged();

        _logger.LogInformation("Toggled favorite: {Title} -> {IsFavorite}", card.Game.Title, card.Game.IsFavorite);
    }

    public Task BackAsync()
    {
        _videoPreview.Stop();
        BackRequested?.Invoke();
        return Task.CompletedTask;
    }

    // ── Media loading ──────────────────────────────────────────────────────

    /// <summary>
    /// Fires all 5 card cover loads in parallel via Task.WhenAll.
    /// The CancellationToken is cancelled by CancelAndRenewNavToken() on the
    /// next navigation action, aborting stale loads immediately.
    /// </summary>
    private async Task LoadVisibleCoversAsync(CancellationToken ct)
    {
        try
        {
            var tasks = VisibleCards.Select(async card =>
            {
                if (ct.IsCancellationRequested) return;
                await card.LoadCoverAsync(_mediaCache, _resolver, ct);
                if (card.VideoPath is null)
                    card.VideoPath = _resolver.ResolveVideo(card.Game);
            });
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { /* navigation moved on — expected */ }
    }

    // ── Windowing ──────────────────────────────────────────────────────────

    /// <summary>
    /// M14 optimization: updates VisibleCards in-place rather than Clear+Add.
    /// This avoids the ItemsControl rebuilding all 5 card UserControls on every
    /// navigation keystroke. Cards are reused — only their DataContext changes.
    ///
    /// Slots that have no card (catalog smaller than 5) are left as-is from
    /// the initial population; the ItemsControl clips to actual count.
    /// </summary>
    private void RefreshVisibleCards()
    {
        if (_allCardsForCategory.Count == 0)
        {
            VisibleCards.Clear();
            SelectedGameTitle = string.Empty;
            SelectedGameSystem = string.Empty;
            SelectedGamePlayers = string.Empty;
            HasNoGames = true;
            return;
        }

        HasNoGames = false;

        int half  = VisibleCardCount / 2;
        int total = _allCardsForCategory.Count;
        int start = _gameIndex - half;
        int end   = start + VisibleCardCount - 1;

        if (start < 0) { start = 0; end = Math.Min(VisibleCardCount - 1, total - 1); }
        if (end >= total) { end = total - 1; start = Math.Max(0, end - VisibleCardCount + 1); }

        int slotCount = end - start + 1;

        // Grow collection if needed (first load, or catalog smaller than 5)
        while (VisibleCards.Count < slotCount)
            VisibleCards.Add(_allCardsForCategory[start + VisibleCards.Count]);

        // Shrink if catalog has fewer than 5 entries
        while (VisibleCards.Count > slotCount)
            VisibleCards.RemoveAt(VisibleCards.Count - 1);

        // In-place update — reuse existing VM slots
        for (int slot = 0; slot < slotCount; slot++)
        {
            int catIdx = start + slot;
            var card = _allCardsForCategory[catIdx];
            card.IsSelected = (catIdx == _gameIndex);

            if (!ReferenceEquals(VisibleCards[slot], card))
                VisibleCards[slot] = card;
            else
            {
                // Same VM in same slot — just refresh selection state
                VisibleCards[slot].IsSelected = card.IsSelected;
            }
        }

        var selected = _allCardsForCategory[_gameIndex];
        SelectedGameTitle  = selected.Title;
        SelectedGameSystem = selected.SystemName;
        SelectedGamePlayers = selected.Players;
    }

    private string ResolveSystemName(string systemId)
        => _systemNames.TryGetValue(systemId, out var name) ? name : systemId;
}
