using UGL.Core.Interfaces;

namespace UGL.Media;

/// <summary>
/// No-op IMediaCache for Milestone 1.
/// Replaced by SkiaMediaCache in Milestone 5.
/// </summary>
internal sealed class NullMediaCache : IMediaCache
{
    public Task<Stream?> GetImageAsync(string absolutePath, CancellationToken ct = default)
        => Task.FromResult<Stream?>(null);

    public void EvictImage(string absolutePath) { }

    public void ClearImageCache() { }

    public Task<Uri?> ResolveVideoUriAsync(string absolutePath, CancellationToken ct = default)
        => Task.FromResult<Uri?>(null);
}
