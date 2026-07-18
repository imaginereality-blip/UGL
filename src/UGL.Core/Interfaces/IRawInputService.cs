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

    bool IsRunning { get; }
}
