using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Data;

/// <summary>
/// No-op IGameRepository for Milestone 1.
/// Replaced by JsonGameRepository in Milestone 2.
/// </summary>
internal sealed class NullGameRepository : IGameRepository
{
    public Task<IReadOnlyList<Game>> GetAllGamesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Game>>(Array.Empty<Game>());

    public Task<IReadOnlyList<Game>> GetGamesByCategoryAsync(string categoryId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Game>>(Array.Empty<Game>());

    public Task<Game?> GetGameByIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult<Game?>(null);

    public Task AddOrUpdateAsync(Game game, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteAsync(string id, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SaveAsync(CancellationToken ct = default)
        => Task.CompletedTask;
}
