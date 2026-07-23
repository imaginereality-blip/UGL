using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Provides read and write access to the shared audio track library
/// (config/tracks.json) — added once, referenced by any number of playlists.
/// </summary>
public interface IAudioTrackRepository
{
    Task<IReadOnlyList<AudioTrack>> GetAllAsync(CancellationToken ct = default);
    Task<AudioTrack?> GetByIdAsync(string id, CancellationToken ct = default);
    Task AddOrUpdateAsync(AudioTrack track, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}
