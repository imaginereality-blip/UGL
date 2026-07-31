using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Manages Win32 RawInput registration, device enumeration, and event dispatch.
///
/// Creates a hidden message-only window on a dedicated thread to receive
/// WM_INPUT messages without requiring a visible window handle.
///
/// All events are raised on the thread that created the message-only window
/// (not the UI thread) — consumers must dispatch to UI thread if needed.
/// </summary>
public interface IRawInputService : IDisposable
{
    /// <summary>Raised when a raw input event is received from any registered device.</summary>
    event EventHandler<RawInputEvent>? RawInputReceived;

    /// <summary>Returns all HID devices currently detected by the system.</summary>
    IReadOnlyList<RawInputDevice> EnumerateDevices();

    /// <summary>Starts the message pump and registers for WM_INPUT on all HID devices.</summary>
    void Start();

    /// <summary>Stops the message pump and unregisters all devices.</summary>
    void Stop();

    /// <summary>
    /// Sets which device types should have their input silently dropped before
    /// RawInputReceived is raised at all — e.g. disabling Lightgun and Wheel while a
    /// fighting game is running, so they can't interfere. Pass an empty set to
    /// re-enable everything (the normal state outside of an active game launch).
    /// </summary>
    void SetDisabledDeviceTypes(IReadOnlySet<RawInputDeviceType> disabledTypes);

    bool IsRunning { get; }

    /// <summary>
    /// Resolves a device's stable RawInput hardware (interface) path to the PnP
    /// device instance ID that Windows device-management APIs (and HidHide's
    /// --dev-hide) expect instead — a different, shorter identifier
    /// (e.g. "HID\VID_20A0&amp;PID_41B6&amp;MI_02&amp;Col01\7&amp;f5175f4&amp;0&amp;0000") than the
    /// interface path. Returns null if it can't be resolved (device disconnected,
    /// or the underlying CM_* call failed).
    /// </summary>
    string? ResolveDeviceInstanceId(string hardwarePath);
}
