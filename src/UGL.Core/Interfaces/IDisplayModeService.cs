using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Temporarily switches the primary display's resolution/refresh rate for the
/// duration of a game session (many emulators, and especially lightgun games, only
/// behave correctly at one specific mode) and restores whatever was active
/// beforehand once the session ends. Only one session is ever active at a time
/// (mirrors IEmulatorLauncher.IsEmulatorRunning), so this tracks a single pending
/// restore rather than a stack.
/// </summary>
public interface IDisplayModeService
{
    /// <summary>
    /// Switches to the given mode. A no-op (returns true, nothing to restore later)
    /// when <paramref name="mode"/> is null or empty. Returns false if the OS
    /// rejected the mode change — the caller should still proceed with launching the
    /// game rather than block on this, same reasoning as a missing BIOS file.
    /// </summary>
    bool Apply(DisplayMode? mode);

    /// <summary>
    /// Restores whatever mode was active before the most recent successful Apply
    /// call. Safe to call even if Apply was never called, or was called with an
    /// empty mode — both are no-ops.
    /// </summary>
    void Restore();
}
