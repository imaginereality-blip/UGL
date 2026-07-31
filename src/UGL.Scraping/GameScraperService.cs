using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Scraping;

/// <summary>Dispatches to the IGameScraperSource matching the requested type.</summary>
public sealed class GameScraperService : IGameScraperService
{
    private readonly IReadOnlyDictionary<ScraperSourceType, IGameScraperSource> _sources;

    public GameScraperService(IEnumerable<IGameScraperSource> sources)
    {
        _sources = sources.ToDictionary(s => s.SourceType);
    }

    public Task<IReadOnlyList<ScraperSearchResult>> SearchAsync(ScraperSourceType source, string title, string? platformHint, CancellationToken ct = default) =>
        _sources.TryGetValue(source, out var s) ? s.SearchAsync(title, platformHint, ct) : Task.FromResult<IReadOnlyList<ScraperSearchResult>>([]);

    public Task<ScraperGameMetadata?> GetDetailsAsync(ScraperSourceType source, string sourceGameId, CancellationToken ct = default) =>
        _sources.TryGetValue(source, out var s) ? s.GetDetailsAsync(sourceGameId, ct) : Task.FromResult<ScraperGameMetadata?>(null);
}
