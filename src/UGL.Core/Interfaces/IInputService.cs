using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Abstracts the low-latency XInput polling loop.
/// Implementations live in UGL.Input and run on a dedicated background thread.
/// The UI subscribes to <see cref="InputReceived"/> on the UI thread
/// (marshalling is handled by the implementation).
/// </summary>
public interface IInputService
{
    /// <summary>
    /// Fired on the UI thread whenever a discrete controller action is detected.
    /// Button-repeat logic (held buttons) is managed internally by the implementation.
    /// </summary>
    event EventHandler<ControllerInputEvent>? InputReceived;

    /// <summary>Starts the background polling loop. Safe to call multiple times (idempotent).</summary>
    void Start();

    /// <summary>Stops the polling loop and releases controller handles.</summary>
    void Stop();

    /// <summary>Returns true if at least one XInput controller is currently connected.</summary>
    bool IsControllerConnected { get; }
}
