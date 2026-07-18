using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;

namespace UGL.Media;

/// <summary>
/// Optimized production IMediaCache.
///
/// M14 performance improvements:
///   - ConcurrentDictionary replaces Dictionary + SemaphoreSlim for reads:
///     cache hits are now lock-free and fully concurrent.
///   - Write lock (SemaphoreSlim) is only acquired when storing a new bitmap,
///     eliminating serialization on the hot path (cache hits).
///   - Parallel bitmap decoding: callers can fire multiple GetBitmapAsync
///     calls simultaneously and they decode on the thread pool concurrently.
///   - Deduplication guard: a secondary ConcurrentDictionary of in-flight
///     decode tasks prevents multiple threads from decoding the same path.
/// </summary>
public sealed class SkiaMediaCache : IMediaCache, IDisposable
{
    private readonly ILogger<SkiaMediaCache> _logger;

    // Lock-free concurrent read cache: path → (Bitmap, lastAccessTick, lastWriteUtc)
    private readonly ConcurrentDictionary<string, (Bitmap Bitmap, long LastAccess, DateTime LastWriteUtc)> _cache = new();

    // In-flight decode tasks — prevents duplicate decodes for the same path
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _inflight = new();

    // File system watchers per directory to provide live-reload behavior
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();

    // Event raised when an image file changes on disk (full absolute path)
    public event Action<string>? ImageChanged;

    // Write lock — only needed for eviction (rare)
    private readonly SemaphoreSlim _evictLock = new(1, 1);

    public int MaxCachedImages { get; set; } = 200;

    public SkiaMediaCache(ILogger<SkiaMediaCache> logger)
    {
        _logger = logger;
    }

    // ── IMediaCache (Stream-based) ─────────────────────────────────────────

    public async Task<Stream?> GetImageAsync(string absolutePath, CancellationToken ct = default)
    {
        if (!File.Exists(absolutePath)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(absolutePath, ct);
            return new MemoryStream(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read image file: {Path}", absolutePath);
            return null;
        }
    }

    public void EvictImage(string absolutePath)
    {
        if (_cache.TryRemove(absolutePath, out var entry))
            entry.Bitmap.Dispose();
    }

    private void EnsureWatcher(string absolutePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(absolutePath);
            if (string.IsNullOrEmpty(dir)) return;

            _watchers.GetOrAdd(dir, d =>
            {
                var w = new FileSystemWatcher(d)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                w.Changed += (s, e) => OnFsChanged(e.FullPath);
                w.Created += (s, e) => OnFsChanged(e.FullPath);
                w.Renamed += (s, e) => OnFsChanged(e.FullPath);
                w.Deleted += (s, e) => OnFsChanged(e.FullPath);
                return w;
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to create FileSystemWatcher");
        }
    }

    private void OnFsChanged(string fullPath)
    {
        try
        {
            EvictImage(fullPath);
            RaiseImageChanged(fullPath);
        }
        catch { }
    }

    /// <summary>
    /// Manually raises ImageChanged for the given path.
    /// Call this after programmatically changing a category's art path
    /// so the home menu reloads the card without needing a file system event.
    /// </summary>
    public void RaiseImageChanged(string absolutePath)
    {
        _logger.LogInformation("[Cache] RaiseImageChanged: {Path}", absolutePath);
        ImageChanged?.Invoke(absolutePath);
    }

    public void ClearImageCache()
    {
        foreach (var key in _cache.Keys.ToList())
            if (_cache.TryRemove(key, out var entry))
                entry.Bitmap.Dispose();
    }

    public Task<Uri?> ResolveVideoUriAsync(string absolutePath, CancellationToken ct = default)
    {
        var uri = File.Exists(absolutePath) ? new Uri(absolutePath) : null;
        return Task.FromResult(uri);
    }

    // ── Extended API: decoded Bitmap for direct UI binding ─────────────────

    /// <summary>
    /// Lock-free on cache hit. On miss, deduplicates in-flight decodes so
    /// multiple callers for the same path share a single Task.
    /// Safe to call concurrently from any number of threads.
    /// </summary>
    public Task<Bitmap?> GetBitmapAsync(string? absolutePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
            return Task.FromResult<Bitmap?>(null);

        // Check file last-write to support live-reload of changed images.
        var lastWrite = File.GetLastWriteTimeUtc(absolutePath);

        // ── Fast path: cache hit and file unchanged (lock-free) ───────────
        if (_cache.TryGetValue(absolutePath, out var cached))
        {
            if (cached.LastWriteUtc == lastWrite)
            {
                _cache.TryUpdate(absolutePath,
                    (cached.Bitmap, Environment.TickCount64, cached.LastWriteUtc),
                    cached);
                return Task.FromResult<Bitmap?>(cached.Bitmap);
            }

            // File changed on disk — evict cached bitmap and let decode proceed
            if (_cache.TryRemove(absolutePath, out var removed))
                removed.Bitmap.Dispose();
        }

        // Ensure we watch this directory so external edits trigger eviction
        EnsureWatcher(absolutePath);

        // ── Slow path: join or start a decode task ─────────────────────────
        // Passes the lastWrite already fetched above straight through, rather than
        // having DecodeAsync call File.GetLastWriteTimeUtc a second time for the
        // same path — that second call was pure waste on every cache miss, since
        // the value never changes between the two calls a few lines apart.
        var decodeTask = _inflight.GetOrAdd(absolutePath, path => DecodeAsync(path, lastWrite, ct));
        return decodeTask;
    }

    private async Task<Bitmap?> DecodeAsync(string path, DateTime lastWriteUtc, CancellationToken ct)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(path);
                return new Bitmap(stream);
            }, ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode image: {Path}", path);
            return null;
        }
        finally
        {
            _inflight.TryRemove(path, out _);
        }

        if (bitmap is null) return null;

        // Race guard: another task may have stored this bitmap already
        if (_cache.TryGetValue(path, out var existing))
        {
            bitmap.Dispose();
            return existing.Bitmap;
        }

        _cache[path] = (bitmap, Environment.TickCount64, lastWriteUtc);

        _logger.LogDebug("Image cached ({Count}/{Max}): {Path}",
            _cache.Count, MaxCachedImages, path);

        if (_cache.Count > MaxCachedImages)
            await EvictOldestAsync();

        return bitmap;
    }

    private async Task EvictOldestAsync()
    {
        await _evictLock.WaitAsync();
        try
        {
            while (_cache.Count > MaxCachedImages)
            {
                var oldest = _cache.MinBy(kv => kv.Value.LastAccess);
                if (_cache.TryRemove(oldest.Key, out var entry))
                {
                    entry.Bitmap.Dispose();
                    _logger.LogDebug("LRU evicted: {Path}", oldest.Key);
                }
            }
        }
        finally { _evictLock.Release(); }
    }

    public void Dispose()
    {
        ClearImageCache();
        _evictLock.Dispose();
    }
}
