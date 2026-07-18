using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels;

/// <summary>
/// Owns the four filter rows (System, Genre, Players, Controller Type).
/// Builds option lists dynamically from the actual game catalog so no
/// filter values are ever hard-coded.
///
/// Usage:
///   await OpenAsync(categoryId)  — call before showing the overlay
///   Navigate with Up/Down (rows) and Left/Right (options within row)
///   Press A → ConfirmFocused, then apply
///   Press B → Dismissed raised, overlay closes with no change
/// </summary>
public sealed partial class FilterOverlayViewModel : ObservableObject
{
    private readonly IGameRepository _gameRepository;
    private readonly ILogger<FilterOverlayViewModel> _logger;

    // Four filter rows in display order.
    public FilterRowViewModel RowSystem  { get; } = new("System");
    public FilterRowViewModel RowGenre   { get; } = new("Genre");
    public FilterRowViewModel RowPlayers { get; } = new("Players");

    /// <summary>All rows in navigation order (Up/Down cycles through these).</summary>
    public List<FilterRowViewModel> Rows { get; }

    private int _focusedRowIndex;

    [ObservableProperty]
    private string _categoryLabel = string.Empty;

    /// <summary>Raised when the user confirms a filter (A on "Apply" or any selection).</summary>
    public event Action<GameFilter>? FilterApplied;

    /// <summary>Raised when the user cancels (B/Escape) without changing the filter.</summary>
    public event Action? Dismissed;

    public FilterOverlayViewModel(
        IGameRepository gameRepository,
        ILogger<FilterOverlayViewModel> logger)
    {
        _gameRepository = gameRepository;
        _logger = logger;

        Rows = [RowSystem, RowGenre, RowPlayers];
    }

    // ── Open ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads option lists for the given category and resets focus to row 0.
    /// Call this each time the overlay is opened.
    /// </summary>
    public async Task OpenAsync(string categoryId, string categoryLabel, GameFilter currentFilter)
    {
        CategoryLabel = categoryLabel;

        var games = await _gameRepository.GetGamesByCategoryAsync(categoryId);

        BuildSystemOptions(games, currentFilter.SystemId);
        BuildGenreOptions(games, currentFilter.Genre);
        BuildPlayerOptions(games, currentFilter.Players);

        _focusedRowIndex = 0;
        RefreshRowFocus();

        foreach (var row in Rows)
            row.RestoreFocusToSelected();

        _logger.LogDebug(
            "Filter overlay opened for category '{Category}' — {Games} games",
            categoryLabel, games.Count);
    }

    // ── Input handlers (called by MainWindowViewModel) ─────────────────────

    public void NavigateUp()
    {
        if (Rows.Count == 0) return;
        _focusedRowIndex = Math.Max(0, _focusedRowIndex - 1);
        RefreshRowFocus();
        Rows[_focusedRowIndex].RestoreFocusToSelected();
    }

    public void NavigateDown()
    {
        if (Rows.Count == 0) return;
        _focusedRowIndex = Math.Min(Rows.Count - 1, _focusedRowIndex + 1);
        RefreshRowFocus();
        Rows[_focusedRowIndex].RestoreFocusToSelected();
    }

    public void NavigateLeft()  => Rows[_focusedRowIndex].MoveFocusLeft();
    public void NavigateRight() => Rows[_focusedRowIndex].MoveFocusRight();

    /// <summary>A button: confirms the focused pill on the current row and applies the full filter.</summary>
    public void Confirm()
    {
        Rows[_focusedRowIndex].ConfirmFocused();
        Apply();
    }

    /// <summary>B button: dismiss without applying.</summary>
    public void Dismiss() => Dismissed?.Invoke();

    /// <summary>Clears all rows back to "All" and re-applies.</summary>
    public void ResetAll()
    {
        foreach (var row in Rows)
            row.Reset();
        Apply();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void Apply()
    {
        var filter = new GameFilter
        {
            SystemId = RowSystem.SelectedValue,
            Genre    = RowGenre.SelectedValue,
            Players  = RowPlayers.SelectedValue is string p
                       && int.TryParse(p, out var n) ? n : null,
        };

        _logger.LogDebug(
            "Filter applied — System:{System} Genre:{Genre} Players:{Players}",
            filter.SystemId ?? "All", filter.Genre ?? "All", filter.Players?.ToString() ?? "All");

        FilterApplied?.Invoke(filter);
    }

    private void RefreshRowFocus()
    {
        for (int i = 0; i < Rows.Count; i++)
            Rows[i].IsFocusedRow = (i == _focusedRowIndex);
    }

    // ── Option builders (dynamic from catalog) ─────────────────────────────

    private void BuildSystemOptions(IReadOnlyList<Game> games, string? activeSystemId)
    {
        RowSystem.ResetOptions();
        RowSystem.Options.Add(new FilterOptionViewModel("All", null) { IsSelected = activeSystemId is null });

        foreach (var systemId in games.Select(g => g.SystemId).Distinct().Order())
        {
            var opt = new FilterOptionViewModel(systemId, systemId)
            {
                IsSelected = systemId.Equals(activeSystemId, StringComparison.OrdinalIgnoreCase)
            };
            RowSystem.Options.Add(opt);
        }
    }

    private void BuildGenreOptions(IReadOnlyList<Game> games, string? activeGenre)
    {
        RowGenre.ResetOptions();
        RowGenre.Options.Add(new FilterOptionViewModel("All", null) { IsSelected = activeGenre is null });

        foreach (var genre in games.Select(g => g.Genre)
                                   .Where(g => !string.IsNullOrWhiteSpace(g))
                                   .Distinct().Order())
        {
            var opt = new FilterOptionViewModel(genre, genre)
            {
                IsSelected = genre.Equals(activeGenre, StringComparison.OrdinalIgnoreCase)
            };
            RowGenre.Options.Add(opt);
        }
    }

    private void BuildPlayerOptions(IReadOnlyList<Game> games, int? activePlayers)
    {
        RowPlayers.ResetOptions();
        RowPlayers.Options.Add(new FilterOptionViewModel("All", null) { IsSelected = activePlayers is null });

        foreach (var count in games.Select(g => g.Players).Distinct().Order())
        {
            var label = count == 1 ? "1 Player" : $"{count} Players";
            var opt = new FilterOptionViewModel(label, count.ToString())
            {
                IsSelected = count == activePlayers
            };
            RowPlayers.Options.Add(opt);
        }
    }
}
