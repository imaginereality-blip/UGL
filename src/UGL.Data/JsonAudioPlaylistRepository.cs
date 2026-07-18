using System.Text.Json;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Data;

internal sealed class JsonAudioPlaylistRepository : IAudioPlaylistRepository
{
    private readonly ILogger<JsonAudioPlaylistRepository> _logger;
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<AudioPlaylist>? _playlists;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
    };

    public JsonAudioPlaylistRepository(ILogger<JsonAudioPlaylistRepository> logger)
    {
        _logger = logger;
        _path = Path.Combine(AppContext.BaseDirectory, "config", "audio.json");
    }

    public async Task<IReadOnlyList<AudioPlaylist>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _playlists!.AsReadOnly();
    }

    public async Task<AudioPlaylist?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _playlists!.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddOrUpdateAsync(AudioPlaylist playlist, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var idx = _playlists!.FindIndex(p => string.Equals(p.Id, playlist.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _playlists[idx] = playlist;
            else _playlists.Add(playlist);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try { _playlists!.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)); }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(_playlists, JsonOptions);
            await File.WriteAllTextAsync(_path, json, ct);
            _logger.LogInformation("audio.json saved ({Count} playlists).", _playlists!.Count);
        }
        finally { _lock.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_playlists is not null) return;
        await _lock.WaitAsync(ct);
        try
        {
            if (_playlists is not null) return;
            if (!File.Exists(_path))
            {
                // Seed with an empty global playlist on first run.
                _playlists = [new AudioPlaylist { Id = "global", Name = "Global Music" }];
                return;
            }
            await using var stream = File.OpenRead(_path);
            _playlists = await JsonSerializer.DeserializeAsync<List<AudioPlaylist>>(stream, JsonOptions, ct) ?? [];
            _logger.LogInformation("audio.json loaded: {Count} playlists.", _playlists.Count);
        }
        finally { _lock.Release(); }
    }
}
