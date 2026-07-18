using System.Runtime.InteropServices;

namespace UGL.Input;

/// <summary>
/// Minimal P/Invoke surface for XInput via xinput1_4.dll (Windows 8+).
/// Only the structs and constants actually used by UGL are defined here.
/// </summary>
internal static class XInput
{
    private const string DllName = "xinput1_4.dll";

    /// <summary>
    /// Retrieves the current state of the specified controller.
    /// Returns 0 (ERROR_SUCCESS) if a controller is connected, 1167 otherwise.
    /// </summary>
    [DllImport(DllName, EntryPoint = "XInputGetState")]
    internal static extern uint GetState(uint dwUserIndex, out XInputState pState);

    internal const uint Success = 0;
    internal const int MaxControllers = 4;

    // ── Button bitmasks (XINPUT_GAMEPAD) ──────────────────────────────────
    internal const ushort DPadUp        = 0x0001;
    internal const ushort DPadDown      = 0x0002;
    internal const ushort DPadLeft      = 0x0004;
    internal const ushort DPadRight     = 0x0008;
    internal const ushort Start         = 0x0010;
    internal const ushort Back          = 0x0020;
    internal const ushort LeftThumb     = 0x0040;
    internal const ushort RightThumb    = 0x0080;
    internal const ushort LeftShoulder  = 0x0100;
    internal const ushort RightShoulder = 0x0200;
    internal const ushort ButtonA       = 0x1000;
    internal const ushort ButtonB       = 0x2000;
    internal const ushort ButtonX       = 0x4000;
    internal const ushort ButtonY       = 0x8000;

    // Trigger threshold: values are 0–255
    internal const byte TriggerThreshold = 100;

    // Thumb-stick dead zone (signed short, range -32768 to 32767)
    internal const short ThumbDeadZone = 8689;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputState
{
    public uint dwPacketNumber;
    public XInputGamepad Gamepad;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputGamepad
{
    public ushort wButtons;
    public byte   bLeftTrigger;
    public byte   bRightTrigger;
    public short  sThumbLX;
    public short  sThumbLY;
    public short  sThumbRX;
    public short  sThumbRY;
}
