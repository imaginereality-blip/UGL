namespace UGL.Core.Models;

/// <summary>
/// Event payload raised by IRawInputService when a WM_INPUT message is processed.
/// Carries device identity, player assignment, and raw axis/button state.
/// </summary>
public sealed class RawInputEvent
{
    /// <summary>Hardware path of the originating device.</summary>
    public required string HardwarePath { get; init; }

    /// <summary>Resolved 1-based player index (0 if unassigned).</summary>
    public int PlayerIndex { get; init; }

    public RawInputDeviceType DeviceType { get; init; }

    // ── Mouse / lightgun / trackball axes ─────────────────────────────────

    /// <summary>Relative X movement (pixels). For lightguns: screen X delta.</summary>
    public int DeltaX { get; init; }

    /// <summary>Relative Y movement (pixels). For lightguns: screen Y delta.</summary>
    public int DeltaY { get; init; }

    /// <summary>Absolute X position (when device reports absolute coords, e.g. some lightguns).</summary>
    public int AbsoluteX { get; init; }

    /// <summary>Absolute Y position.</summary>
    public int AbsoluteY { get; init; }

    public bool IsAbsolute { get; init; }

    // ── Button states ──────────────────────────────────────────────────────

    /// <summary>Bitmask of currently pressed buttons. Bit 0 = button 1, etc.</summary>
    public uint ButtonsDown { get; init; }

    /// <summary>Bitmask of buttons that transitioned to pressed this event.</summary>
    public uint ButtonsPressed { get; init; }

    /// <summary>Bitmask of buttons that transitioned to released this event.</summary>
    public uint ButtonsReleased { get; init; }

    // ── Wheel / spinner axes ───────────────────────────────────────────────

    /// <summary>Wheel rotation delta (positive = forward). Spinner uses this as rotation count.</summary>
    public int WheelDelta { get; init; }

    // ── HID joystick/gamepad button state (RIM_TYPE_HID only) ─────────────────
    // Devices like the X-Gunner that report numbered buttons/D-pad via a generic
    // HID joystick collection rather than as mouse buttons — resolved via
    // HidP_GetUsages against the device's own HID report descriptor. Always empty
    // for RIM_TYPE_MOUSE events (use ButtonsPressed/ButtonsDown for those instead).

    /// <summary>HID usage IDs newly pressed this event (not held over from the
    /// previous report).</summary>
    public IReadOnlyList<ushort> HidUsagesNewlyPressed { get; init; } = [];

    /// <summary>Every HID usage ID currently held, not just the new ones — for a
    /// live "what's pressed right now" display.</summary>
    public IReadOnlyList<ushort> HidUsagesHeld { get; init; } = [];
}
