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
    /// when set). analogProfile, when supplied, contributes global analog dead
    /// zone/sensitivity binds (see ControllerProfile — Wheel/Gamepad axis feel,
    /// Settings → Controllers). Deliberately NOT a full button-remap translation:
    /// ControllerProfile.ButtonMap remaps UGL's own menu-navigation actions to
    /// physical XInput bits, which has no reliable correspondence to RetroArch's
    /// own per-core joypad button indexing (a separate, driver-dependent numbering
    /// UGL has no correlation data for) — attempting that translation would risk
    /// silently generating incorrect in-game bindings, which is worse than not
    /// generating any. Returns the absolute path to the generated file.
    /// </summary>
    Task<string> GenerateAsync(
        Game game,
        Emulator emulator,
        GameSystem system,
        ControllerProfile? analogProfile = null,
        CancellationToken ct = default);

    /// <summary>Deletes the generated override file if it exists.</summary>
    void Cleanup();
}
