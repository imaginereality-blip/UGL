namespace UGL.Core.Models;

using System.Text.Json.Serialization;

/// <summary>Categories of raw HID peripheral relevant to arcade cabinets.</summary>
public enum RawInputDeviceType
{
    Unknown,
    Gamepad,     // XInput/DirectInput gamepad (fallback for non-XInput pads)
    Lightgun,    // Sinden, Gun4IR, AimTrak, etc.
    Wheel,       // Steering wheel / pedals
    Spinner,     // Optical spinner (Tempest, etc.)
    Trackball,   // Optical trackball
    Keyboard,    // Keyboard (for completeness)
    Mouse,       // Generic mouse (used as lightgun fallback)
}

/// <summary>
/// Represents one physical HID device detected by Win32 RawInput.
/// Persisted to controllers.json to maintain stable player index assignments
/// across reboots (devices are identified by their hardware path, not handle,
/// since handles change between sessions).
/// </summary>
public sealed class RawInputDevice
{
    /// <summary>Stable hardware path from GetRawInputDeviceInfo (RIDI_DEVICENAME). Used as persistence key.</summary>
    public required string HardwarePath { get; init; }

    /// <summary>Friendly display name from SetupDi device info.</summary>
    public string FriendlyName { get; set; } = string.Empty;

    public RawInputDeviceType DeviceType { get; set; } = RawInputDeviceType.Unknown;

    /// <summary>
    /// 1-based player index assigned by the user. 0 = unassigned.
    /// For lightguns: Player 1 = gun 1, Player 2 = gun 2, etc.
    /// </summary>
    public int PlayerIndex { get; set; } = 0;

    /// <summary>Whether this device is currently connected (runtime only, not persisted).</summary>
    [JsonIgnore]
    public bool IsConnected { get; set; } = false;

    /// <summary>
    /// Raw Win32 device handle (runtime only, changes each session).
    /// Excluded from JSON: System.Text.Json cannot serialize nint/IntPtr,
    /// and this value is meaningless across process restarts anyway.
    /// </summary>
    [JsonIgnore]
    public nint RuntimeHandle { get; set; } = 0;
}
