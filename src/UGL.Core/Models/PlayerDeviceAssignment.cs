namespace UGL.Core.Models;

/// <summary>
/// A ranked list of specific devices (by RawInputDevice.HardwarePath) that should be
/// available for one player slot in a specific game — e.g. "Player 1: my Logitech
/// wheel, or my Xbox pad if the wheel isn't connected." At launch, the first entry
/// that's currently connected is kept visible; every other assignable-type device
/// not selected for any slot in this game is hidden via HidHide (see
/// ProcessEmulatorLauncher), so only the intended device(s) are ever visible to the
/// emulator/game.
///
/// UGL cannot force an arbitrary third-party emulator/game to treat a specific
/// physical device as "its" Player 1/2/etc. internally (that's the emulator's own
/// input configuration) — hiding everything else is the honest, universal mechanism
/// available: if only the intended devices are visible, there's nothing else for the
/// game to enumerate, which is what actually makes "assign this wheel to this game"
/// or "these two pads to this 2-player game" work in practice.
/// </summary>
public sealed class PlayerDeviceAssignment
{
    /// <summary>1-based, purely for grouping/display in the Games editor — not sent
    /// to the emulator in any form.</summary>
    public int PlayerIndex { get; set; } = 1;

    /// <summary>Ranked by preference, most-preferred first.</summary>
    public List<string> PreferredHardwarePaths { get; set; } = [];
}
