using UGL.Core.Models;

namespace UGL.Core.Interfaces;

public interface IAudioPlaylistRepository
{
    Task<IReadOnlyList<AudioPlaylist>> GetAllAsync(CancellationToken ct = default);
    Task<AudioPlaylist?> GetByIdAsync(string id, CancellationToken ct = default);
    Task AddOrUpdateAsync(AudioPlaylist playlist, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}
