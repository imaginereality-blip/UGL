using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Provides read and write access to the game catalog.
/// Implementations live in UGL.Data and are injected via DI.
/// All methods are async to avoid blocking the UI thread during disk I/O.
/// </summary>
public interface IGameRepository
{
    Task<IReadOnlyList<Game>> GetAllGamesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Game>> GetGamesByCategoryAsync(string categoryId, CancellationToken cancellationToken = default);
    Task<Game?> GetGameByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Adds a new game or replaces an existing one with the same Id.</summary>
    Task AddOrUpdateAsync(Game game, CancellationToken cancellationToken = default);

    /// <summary>Removes the game with the given Id. No-op if not found.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}
