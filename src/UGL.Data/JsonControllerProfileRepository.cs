using System.Text.Json;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Data;

/// <summary>
/// Persists ControllerProfile entries in {exeDir}/config/controllerprofiles.json —
/// a list keyed by HardwarePath, same "stable RawInput identifier" convention
/// PeripheralRegistry (config/controllers.json) uses. Lazy-loaded and cached in
/// memory like JsonHookSettingsRepository/JsonScraperSettingsRepository, since
/// XInputPollingService reads this on every poll tick (~60Hz) and can't afford a
/// disk read that often.
/// </summary>
public sealed class JsonControllerProfileRepository : IControllerProfileRepository
{
    private readonly ILogger<JsonControllerProfileRepository> _logger;
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private List<ControllerProfile>? _profiles;

    public JsonControllerProfileRepository(ILogger<JsonControllerProfileRepository> logger)
    {
        _logger = logger;
        _path = Path.Combine(AppContext.BaseDirectory, "config", "controllerprofiles.json");
    }

    public async Task<IReadOnlyList<ControllerProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _profiles!.AsReadOnly();
    }

    public async Task<ControllerProfile?> GetByHardwarePathAsync(string hardwarePath, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        var forDevice = _profiles!.Where(p => p.HardwarePath.Equals(hardwarePath, StringComparison.OrdinalIgnoreCase)).ToList();
        // Prefer the flagged-active profile; fall back to the first one found if none
        // is flagged (defensive — shouldn't happen via this repository's own writes,
        // but a hand-edited config/controllerprofiles.json could hit this).
        return forDevice.FirstOrDefault(p => p.IsActive) ?? forDevice.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ControllerProfile>> GetProfilesForDeviceAsync(string hardwarePath, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _profiles!
            .Where(p => p.HardwarePath.Equals(hardwarePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveAsync(ControllerProfile profile, bool setActive = true, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            _profiles!.RemoveAll(p =>
                p.HardwarePath.Equals(profile.HardwarePath, StringComparison.OrdinalIgnoreCase) &&
                p.ProfileName.Equals(profile.ProfileName, StringComparison.OrdinalIgnoreCase));

            if (setActive)
            {
                foreach (var p in _profiles.Where(p => p.HardwarePath.Equals(profile.HardwarePath, StringComparison.OrdinalIgnoreCase)))
                    p.IsActive = false;
                profile.IsActive = true;
            }

            _profiles.Add(profile);
            await WriteAsync(ct);
            _logger.LogInformation("Controller profile saved: {Name}/{Profile} ({Type}).",
                profile.FriendlyName, profile.ProfileName, profile.DeviceType);
        }
        finally { _lock.Release(); }
    }

    public async Task SetActiveProfileAsync(string hardwarePath, string profileName, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            bool found = false;
            foreach (var p in _profiles!.Where(p => p.HardwarePath.Equals(hardwarePath, StringComparison.OrdinalIgnoreCase)))
            {
                p.IsActive = p.ProfileName.Equals(profileName, StringComparison.OrdinalIgnoreCase);
                if (p.IsActive) found = true;
            }
            if (found) await WriteAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteProfileAsync(string hardwarePath, string profileName, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var removed = _profiles!.RemoveAll(p =>
                p.HardwarePath.Equals(hardwarePath, StringComparison.OrdinalIgnoreCase) &&
                p.ProfileName.Equals(profileName, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed) return;

            var remaining = _profiles.Where(p => p.HardwarePath.Equals(hardwarePath, StringComparison.OrdinalIgnoreCase)).ToList();
            if (remaining.Count > 0 && !remaining.Any(p => p.IsActive))
                remaining[0].IsActive = true;

            await WriteAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string hardwarePath, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            _profiles!.RemoveAll(p => p.HardwarePath.Equals(hardwarePath, StringComparison.OrdinalIgnoreCase));
            await WriteAsync(ct);
        }
        finally { _lock.Release(); }
    }

    private async Task WriteAsync(CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(_profiles, JsonOptions);
        var dir = Path.GetDirectoryName(_path);
        if (dir is not null) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(_path, json, ct);
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_profiles is not null) return;
        await _lock.WaitAsync(ct);
        try
        {
            if (_profiles is not null) return;
            if (!File.Exists(_path))
            {
                _logger.LogInformation("controllerprofiles.json not found at {Path}. Starting with no profiles.", _path);
                _profiles = [];
                return;
            }

            await using var stream = File.OpenRead(_path);
            _profiles = await JsonSerializer.DeserializeAsync<List<ControllerProfile>>(stream, JsonOptions, ct) ?? [];
            _logger.LogInformation("controllerprofiles.json loaded: {Count} profiles.", _profiles.Count);
        }
        finally { _lock.Release(); }
    }
}
