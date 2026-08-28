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
            RecomputeDisplayLabels();
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

        RecomputeDisplayLabels();
    }

    /// <summary>
    /// Recomputes DisplayLabel for every known device. Two controllers Windows
    /// reports with the identical FriendlyName (the normal case for two of the same
    /// model — there's no vendor serial-number-style differentiator surfaced here)
    /// are otherwise indistinguishable everywhere they're listed: Settings →
    /// Controllers, the Peripherals tab, and the Games editor's Player Device
    /// Assignments picker. Prefers the user's own assigned Player index when set
    /// (e.g. "X-Gunner (P1)"/"X-Gunner (P2)" once assigned in the Peripherals tab —
    /// matches the label the physical device is already effectively known by for
    /// game setup), falling back to a stable ordinal ("#1"/"#2", ordered by
    /// HardwarePath so it doesn't reshuffle between rescans) before that's done. A
    /// name that's already unique among known devices is left unchanged — no need
    /// to clutter a single Racing Wheel with "#1".
    /// </summary>
    private void RecomputeDisplayLabels()
    {
        foreach (var group in _devices.GroupBy(d => (d.DeviceType, Name: d.FriendlyName), NameGroupComparer.Instance))
        {
            var siblings = group.OrderBy(d => d.HardwarePath, StringComparer.OrdinalIgnoreCase).ToList();
            if (siblings.Count <= 1)
            {
                if (siblings.Count == 1) siblings[0].DisplayLabel = siblings[0].FriendlyName;
                continue;
            }

            for (int i = 0; i < siblings.Count; i++)
            {
                var d = siblings[i];
                d.DisplayLabel = d.PlayerIndex > 0
                    ? $"{d.FriendlyName} (P{d.PlayerIndex})"
                    : $"{d.FriendlyName} #{i + 1}";
            }
        }
    }

    private sealed class NameGroupComparer : IEqualityComparer<(RawInputDeviceType DeviceType, string Name)>
    {
        public static readonly NameGroupComparer Instance = new();
        public bool Equals((RawInputDeviceType DeviceType, string Name) x, (RawInputDeviceType DeviceType, string Name) y) =>
            x.DeviceType == y.DeviceType && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((RawInputDeviceType DeviceType, string Name) obj) =>
            HashCode.Combine(obj.DeviceType, obj.Name.ToUpperInvariant());
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
        RecomputeDisplayLabels();
        await SaveAsync(ct);
        _logger.LogInformation(
            "Player {Index} assigned to: {Name}", playerIndex, device.FriendlyName);
    }

    public async Task RemoveDeviceAsync(string hardwarePath, CancellationToken ct = default)
    {
        _devices.RemoveAll(d =>
            d.HardwarePath.Equals(hardwarePath, StringComparison.OrdinalIgnoreCase));
        RecomputeDisplayLabels();
        await SaveAsync(ct);
    }
}
