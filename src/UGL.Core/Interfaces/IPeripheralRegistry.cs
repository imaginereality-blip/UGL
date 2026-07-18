using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Maintains stable player index assignments for HID peripherals.
/// Assignments are keyed by hardware path (stable across reboots)
/// and persisted to config/controllers.json.
/// </summary>
public interface IPeripheralRegistry
{
    /// <summary>All known devices (connected and previously seen).</summary>
    IReadOnlyList<RawInputDevice> KnownDevices { get; }

    /// <summary>
    /// Merges a live-enumerated device list with the persisted registry.
    /// New devices are added as unassigned; previously seen devices retain
    /// their player index and have IsConnected updated.
    /// </summary>
    void MergeConnectedDevices(IReadOnlyList<RawInputDevice> connectedDevices);

    /// <summary>Returns the player index for a device by hardware path. Returns 0 if unassigned.</summary>
    int GetPlayerIndex(string hardwarePath);

    /// <summary>Assigns a player index to a device. Persists immediately.</summary>
    Task AssignPlayerIndexAsync(string hardwarePath, int playerIndex, CancellationToken ct = default);

    /// <summary>Removes a device from the registry.</summary>
    Task RemoveDeviceAsync(string hardwarePath, CancellationToken ct = default);

    /// <summary>Persists the current registry state to controllers.json.</summary>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>Loads the registry from controllers.json.</summary>
    Task LoadAsync(CancellationToken ct = default);
}
