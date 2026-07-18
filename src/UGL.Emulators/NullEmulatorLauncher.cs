using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Emulators;

/// <summary>
/// No-op IEmulatorLauncher for Milestone 1.
/// Replaced by ProcessEmulatorLauncher in Milestone 8.
/// </summary>
internal sealed class NullEmulatorLauncher : IEmulatorLauncher
{
#pragma warning disable CS0067
    public event EventHandler<int>? EmulatorExited;
#pragma warning restore CS0067

    public bool IsEmulatorRunning => false;

    public Task<int> LaunchGameAsync(Game game, CancellationToken ct = default)
        => Task.FromResult(-1);

    public Task KillCurrentEmulatorAsync()
        => Task.CompletedTask;
}
