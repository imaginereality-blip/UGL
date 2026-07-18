using System.Text.Json;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Input;

/// <summary>
/// Persists HID device-to-player index mappings in config/controllers.json.
/// Devices are keyed by their stable hardware path from Win32 RawInput.
/// </summary>
public sealed class PeripheralRegistry : IPeripheralRegistry
{
    private readonly ILogger<PeripheralRegistry> _logger;
    private readonly string _registryPath;
    private readonly List<RawInputDevice> _devices = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
    };

    public IReadOnlyList<RawInputDevice> KnownDevices => _devices.AsReadOnly();

    public PeripheralRegistry(ILogger<PeripheralRegistry> logger)
    {
        _logger = logger;
        _registryPath = Path.Combine(AppContext.BaseDirectory, "config", "controllers.json");
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_registryPath))
        {
            _logger.LogInformation("controllers.json not found — starting with empty registry.");
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_registryPath);
            var loaded = await JsonSerializer.DeserializeAsync<List<RawInputDevice>>(
                stream, JsonOptions, ct) ?? [];
            _devices.Clear();
            _devices.AddRange(loaded);
            _logger.LogInformation("Peripheral registry loaded: {Count} devices.", _devices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load controllers.json.");
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Only persist the fields that should survive a reboot
            var toSave = _devices.Select(d => new RawInputDevice
            {
                HardwarePath = d.HardwarePath,
                FriendlyName = d.FriendlyName,
                DeviceType   = d.DeviceType,
                PlayerIndex  = d.PlayerIndex,
                // IsConnected and RuntimeHandle are runtime-only — not persisted
            }).ToList();

            var json = JsonSerializer.Serialize(toSave, JsonOptions);
            await File.WriteAllTextAsync(_registryPath, json, ct);
            _logger.LogInformation("Peripheral registry saved ({Count} devices).", _devices.Count);
        }
        finally { _lock.Release(); }
    }

    public void MergeConnectedDevices(IReadOnlyList<RawInputDevice> connectedDevices)
    {
        // Mark all as disconnected first
        foreach (var d in _devices) { d.IsConnected = false; d.RuntimeHandle = 0; }

        foreach (var live in connectedDevices)
        {
            var existing = _devices.FirstOrDefault(
                d => d.HardwarePath.Equals(live.HardwarePath, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                // Update runtime state, preserve persisted player index
                existing.IsConnected    = true;
                existing.RuntimeHandle  = live.RuntimeHandle;
                existing.FriendlyName   = live.FriendlyName; // may improve over time
                existing.DeviceType     = live.DeviceType;
            }
            else
            {
                // New device — add as unassigned
                live.IsConnected = true;
                _devices.Add(live);
                _logger.LogInformation(
                    "New peripheral detected: {Name} ({Type})", live.FriendlyName, live.DeviceType);
            }
        }
    }

    public int GetPlayerIndex(string hardwarePath) =>
        _devices.FirstOrDefault(d =>
            d.HardwarePath.Equals(hardwarePath, StringComparison.OrdinalIgnoreCase))
            ?.PlayerIndex ?? 0;

    public async Task AssignPlayerIndexAsync(string hardwarePath, int playerIndex, CancellationToken ct = default)
    {
        var device = _devices.FirstOrDefault(
            d => d.HardwarePath.Equals(hardwarePath, StringComparison.OrdinalIgnoreCase));
        if (device is null) return;

        device.PlayerIndex = playerIndex;
        await SaveAsync(ct);
        _logger.LogInformation(
            "Player {Index} assigned to: {Name}", playerIndex, device.FriendlyName);
    }

    public async Task RemoveDeviceAsync(string hardwarePath, CancellationToken ct = default)
    {
        _devices.RemoveAll(d =>
            d.HardwarePath.Equals(hardwarePath, StringComparison.OrdinalIgnoreCase));
        await SaveAsync(ct);
    }
}
