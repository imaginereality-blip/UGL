using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Dispatches to whichever IGameScraperSource matches the requested
/// ScraperSourceType. The UI only ever talks to this, never to an individual source
/// directly — keeps "which of the three sources" a simple enum choice at the call
/// site instead of DI-resolving a specific implementation type.
/// </summary>
public interface IGameScraperService
{
    Task<IReadOnlyList<ScraperSearchResult>> SearchAsync(ScraperSourceType source, string title, string? platformHint, CancellationToken ct = default);
    Task<ScraperGameMetadata?> GetDetailsAsync(ScraperSourceType source, string sourceGameId, CancellationToken ct = default);
}
