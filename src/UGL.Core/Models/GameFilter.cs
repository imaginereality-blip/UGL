namespace UGL.Core.Models;

/// <summary>
/// Immutable snapshot of the user's active filter selections.
/// Passed from FilterOverlayViewModel to GameBrowserViewModel.SetCategoryAsync.
/// All fields are nullable — null means "no filter applied for this dimension".
/// </summary>
public sealed record GameFilter
{
    /// <summary>Filter by SystemId. Null = all systems.</summary>
    public string? SystemId { get; init; }

    /// <summary>Filter by Genre string. Null = all genres.</summary>
    public string? Genre { get; init; }

    /// <summary>Filter by minimum player count. Null = any player count.</summary>
    public int? Players { get; init; }

    /// <summary>True when no filters are active (all fields null).</summary>
    public bool IsEmpty =>
        SystemId is null &&
        Genre is null &&
        Players is null;

    /// <summary>Returns a copy of this filter with all fields cleared.</summary>
    public static GameFilter Empty { get; } = new();

    /// <summary>
    /// Applies this filter to a list of games and returns only those that match.
    /// </summary>
    public IEnumerable<Game> Apply(IEnumerable<Game> games)
    {
        if (IsEmpty) return games;

        return games.Where(g =>
            (SystemId is null || g.SystemId.Equals(SystemId, StringComparison.OrdinalIgnoreCase)) &&
            (Genre    is null || g.Genre.Equals(Genre,       StringComparison.OrdinalIgnoreCase)) &&
            (Players  is null || g.Players >= Players));
    }
}
