using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Input;

/// <summary>
/// No-op IInputService for Milestone 1.
/// Replaced by XInputPollingService in Milestone 3.
/// </summary>
internal sealed class NullInputService : IInputService
{
#pragma warning disable CS0067 // Event never used — intentional in null implementation
    public event EventHandler<ControllerInputEvent>? InputReceived;
#pragma warning restore CS0067

    public bool IsControllerConnected => false;

    public void Start() { }

    public void Stop() { }
}
