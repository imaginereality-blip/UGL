using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Encapsulates process spawning, CLI argument construction, and focus
/// management for launching games via external emulators.
/// Implementations live in UGL.Emulators.
/// </summary>
public interface IEmulatorLauncher
{
    /// <summary>Fired when an emulator process exits, returning focus to UGL.</summary>
    event EventHandler<int>? EmulatorExited;

    /// <summary>
    /// Launches the emulator configured for the given game.
    /// The UGL UI should minimize or hide itself while the emulator is running.
    /// </summary>
    /// <returns>The launched process ID, or -1 if launch failed.</returns>
    Task<int> LaunchGameAsync(Game game, CancellationToken cancellationToken = default);

    /// <summary>Returns true if an emulator process is currently running.</summary>
    bool IsEmulatorRunning { get; }

    /// <summary>Terminates the currently running emulator process, if any.</summary>
    Task KillCurrentEmulatorAsync();
}
