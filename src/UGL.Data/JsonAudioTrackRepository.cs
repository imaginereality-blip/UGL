using System.Text.Json;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Data;

public sealed class JsonAudioTrackRepository : IAudioTrackRepository
{
    private readonly ILogger<JsonAudioTrackRepository> _logger;
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<AudioTrack>? _tracks;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
    };

    public JsonAudioTrackRepository(ILogger<JsonAudioTrackRepository> logger)
    {
        _logger = logger;
        _path = Path.Combine(AppContext.BaseDirectory, "config", "tracks.json");
    }

    public async Task<IReadOnlyList<AudioTrack>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _tracks!.AsReadOnly();
    }

    public async Task<AudioTrack?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _tracks!.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddOrUpdateAsync(AudioTrack track, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var idx = _tracks!.FindIndex(t => string.Equals(t.Id, track.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _tracks[idx] = track;
            else _tracks.Add(track);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try { _tracks!.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)); }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var configDir = Path.GetDirectoryName(_path);
            if (configDir is not null) Directory.CreateDirectory(configDir);
            var json = JsonSerializer.Serialize(_tracks, JsonOptions);
            await File.WriteAllTextAsync(_path, json, ct);
            _logger.LogInformation("tracks.json saved ({Count} tracks).", _tracks!.Count);
        }
        finally { _lock.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_tracks is not null) return;
        await _lock.WaitAsync(ct);
        try
        {
            if (_tracks is not null) return;
            if (!File.Exists(_path))
            {
                _tracks = [];
                return;
            }
            await using var stream = File.OpenRead(_path);
            _tracks = await JsonSerializer.DeserializeAsync<List<AudioTrack>>(stream, JsonOptions, ct) ?? [];
            _logger.LogInformation("tracks.json loaded: {Count} tracks.", _tracks.Count);
        }
        finally { _lock.Release(); }
    }
}
