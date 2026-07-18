using System.Text.Json;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Data;

internal sealed class JsonEmulatorRepository : IEmulatorRepository
{
    private readonly ILogger<JsonEmulatorRepository> _logger;
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<Emulator>? _emulators;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
    };

    public JsonEmulatorRepository(ILogger<JsonEmulatorRepository> logger)
    {
        _logger = logger;
        _path = Path.Combine(AppContext.BaseDirectory, "config", "emulators.json");
    }

    public async Task<IReadOnlyList<Emulator>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _emulators!.AsReadOnly();
    }

    public async Task<Emulator?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _emulators!.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddOrUpdateAsync(Emulator emulator, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var idx = _emulators!.FindIndex(e => string.Equals(e.Id, emulator.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _emulators[idx] = emulator;
            else _emulators.Add(emulator);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try { _emulators!.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)); }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(_emulators, JsonOptions);
            await File.WriteAllTextAsync(_path, json, ct);
            _logger.LogInformation("emulators.json saved ({Count} entries).", _emulators!.Count);
        }
        finally { _lock.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_emulators is not null) return;
        await _lock.WaitAsync(ct);
        try
        {
            if (_emulators is not null) return;
            if (!File.Exists(_path)) { _emulators = []; return; }
            await using var stream = File.OpenRead(_path);
            _emulators = await JsonSerializer.DeserializeAsync<List<Emulator>>(stream, JsonOptions, ct) ?? [];
            _logger.LogInformation("emulators.json loaded: {Count} entries.", _emulators.Count);
        }
        finally { _lock.Release(); }
    }
}
