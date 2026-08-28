using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Reads and writes ControllerProfile entries (config/controllerprofiles.json) — one
/// or more NAMED profiles per physical controller (Save As / Load in the Controllers
/// tab), applied globally rather than per-game. See ControllerProfile's own doc
/// comment for scope.
/// </summary>
public interface IControllerProfileRepository
{
    Task<IReadOnlyList<ControllerProfile>> GetAllAsync(CancellationToken ct = default);

    /// <summary>The ACTIVE profile for this device (ControllerProfile.IsActive) — the
    /// one XInputPollingService/RetroArch config generation actually apply. Null if
    /// this device has never been configured — callers should fall back to hardcoded
    /// defaults, not treat this as an error.</summary>
    Task<ControllerProfile?> GetByHardwarePathAsync(string hardwarePath, CancellationToken ct = default);

    /// <summary>Every named profile saved for this device (Default plus any Save As
    /// results), for the Controllers tab's Load dialog.</summary>
    Task<IReadOnlyList<ControllerProfile>> GetProfilesForDeviceAsync(string hardwarePath, CancellationToken ct = default);

    /// <summary>Saves profile under its own (HardwarePath, ProfileName) key,
    /// overwriting any existing profile with the same pair. When setActive is true
    /// (the default — matches the single-profile-per-device behavior every caller
    /// outside the Controllers tab still relies on), it also becomes this device's
    /// active profile, clearing the flag on every other profile for the same device.</summary>
    Task SaveAsync(ControllerProfile profile, bool setActive = true, CancellationToken ct = default);

    /// <summary>Switches which already-saved profile is active for this device
    /// without changing any profile's own contents — used by Load. No-op if
    /// profileName doesn't match a saved profile for this device.</summary>
    Task SetActiveProfileAsync(string hardwarePath, string profileName, CancellationToken ct = default);

    /// <summary>Deletes one named profile. If it was the active one and another
    /// profile remains for the same device, an arbitrary remaining profile becomes
    /// active so GetByHardwarePathAsync keeps resolving to something.</summary>
    Task DeleteProfileAsync(string hardwarePath, string profileName, CancellationToken ct = default);

    /// <summary>Deletes every saved profile for this device.</summary>
    Task DeleteAsync(string hardwarePath, CancellationToken ct = default);
}
