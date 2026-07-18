using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Generates a RetroArch override config file (ugl_override.cfg) from
/// an Emulator's RetroArchConfig and the target Game before launch.
///
/// The generated file is passed to RetroArch via --appendconfig so it
/// overrides the user's own retroarch.cfg without modifying it.
/// </summary>
public interface IRetroArchConfigGenerator
{
    /// <summary>
    /// Builds ugl_override.cfg for the given game and emulator. system is used to
    /// resolve the default bezel/overlay art (Game.BezelOverridePath takes priority
    /// when set). Returns the absolute path to the generated file.
    /// </summary>
    Task<string> GenerateAsync(
        Game game,
        Emulator emulator,
        GameSystem system,
        CancellationToken ct = default);

    /// <summary>Deletes the generated override file if it exists.</summary>
    void Cleanup();
}
