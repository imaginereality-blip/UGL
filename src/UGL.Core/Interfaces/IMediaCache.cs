namespace UGL.Core.Interfaces;

/// <summary>
/// Manages async, lazy-loaded, GPU-friendly image and video asset caching.
/// Implementations live in UGL.Media.
/// </summary>
public interface IMediaCache
{
    /// <summary>
    /// Asynchronously retrieves a decoded bitmap stream for the given file path.
    /// Returns null if the file does not exist or cannot be decoded.
    /// Results are cached in memory by path after first load.
    /// </summary>
    Task<Stream?> GetImageAsync(string absolutePath, CancellationToken cancellationToken = default);

    /// <summary>Evicts a single image from the in-memory cache.</summary>
    void EvictImage(string absolutePath);

    /// <summary>Clears the entire image cache (e.g. on theme change).</summary>
    void ClearImageCache();

    /// <summary>
    /// Returns the absolute URI suitable for the video player to open.
    /// Video is not pre-buffered; this method resolves and validates the path only.
    /// </summary>
    Task<Uri?> ResolveVideoUriAsync(string absolutePath, CancellationToken cancellationToken = default);
}
