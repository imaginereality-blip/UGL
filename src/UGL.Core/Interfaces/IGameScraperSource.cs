using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// One pluggable metadata/image source (IGDB, ScreenScraper, TheGamesDB — see
/// UGL.Scraping). Implementations own their own auth and request shape; callers only
/// ever see the two methods below.
/// </summary>
public interface IGameScraperSource
{
    ScraperSourceType SourceType { get; }

    /// <summary>
    /// Free-text title search, optionally narrowed to a platform/system (e.g.
    /// "Dreamcast") — without this, a title shared across multiple platforms (very
    /// common for long-running franchises/ports) can match the wrong version's
    /// metadata/art. Implementations that can't filter server-side should still
    /// accept the parameter and ignore it rather than fail. Returns an empty array
    /// (never throws for a "no results" case) if nothing matched or the source isn't
    /// configured/reachable.
    /// </summary>
    Task<IReadOnlyList<ScraperSearchResult>> SearchAsync(string title, string? platformHint, CancellationToken ct = default);

    /// <summary>Fetches full metadata for one specific result from SearchAsync.
    /// Returns null if the source isn't configured/reachable or the id no longer resolves.</summary>
    Task<ScraperGameMetadata?> GetDetailsAsync(string sourceGameId, CancellationToken ct = default);
}
