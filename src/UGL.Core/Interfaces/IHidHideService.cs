namespace UGL.Core.Interfaces;

/// <summary>
/// Wraps HidHide (nefarius/HidHide, https://github.com/nefarius/HidHide) — a HID
/// device firewall driver — via its bundled HidHideCLI.exe, to provide a *real*
/// per-game peripheral disable. RawInputService.SetDisabledDeviceTypes only discards
/// events before UGL's own handlers see them; it has no effect on what the actual
/// emulator/game process reads, since the emulator talks to Windows directly. HidHide
/// hides a device from every process except an explicit allow-list, which is the
/// only way to make "disabled for this game" mean something to the game itself.
///
/// Only one game session is ever active at a time (mirrors IEmulatorLauncher.
/// IsEmulatorRunning), so this tracks a single pending restore rather than a stack —
/// same convention as IDisplayModeService.
/// </summary>
public interface IHidHideService
{
    /// <summary>True when a CLI path is configured and the executable exists.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Hides the given device instance IDs from every process except UGL itself.
    /// A no-op (returns true, nothing to restore later) when the list is empty or
    /// IsAvailable is false. Returns false if HidHideCLI.exe reported an error —
    /// callers should log and proceed with the game launch regardless, same
    /// reasoning as a missing BIOS file never blocking a launch.
    /// </summary>
    Task<bool> HideAsync(IReadOnlyCollection<string> deviceInstanceIds, CancellationToken ct = default);

    /// <summary>
    /// Un-hides whatever the most recent successful HideAsync call hid. Safe to call
    /// even if HideAsync was never called, or was called with an empty list.
    /// </summary>
    Task RestoreAsync(CancellationToken ct = default);
}
