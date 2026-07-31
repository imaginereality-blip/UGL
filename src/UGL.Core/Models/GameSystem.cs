namespace UGL.Core.Models;

/// <summary>
/// Represents a hardware platform (e.g. Arcade, NES, SNES).
///
/// RomPath: optional per-system ROM directory override.
/// When set, ROMs for this system are loaded from this path regardless
/// of the global RomsRootPath setting. Supports any drive or network share.
/// Leave empty to use {AppSettings.RomsRootPath}\{Id}\ as the default.
///
/// Examples:
///   Empty  → C:\UltimateGameLauncher\roms\arcade\
///   D:\Pinball\Tables → D:\Pinball\Tables\
///   \\NAS\Games\NES   → \\NAS\Games\NES\
/// </summary>
public sealed class GameSystem
{
    public required string Id   { get; set; }
    public required string Name { get; set; }

    /// <summary>
    /// Optional ROM directory for this system. Absolute or relative to exe.
    /// Overrides AppSettings.RomsRootPath for this system only.
    /// Any drive, any path, any network share — no restrictions.
    /// </summary>
    public string RomPath { get; set; } = string.Empty;

    /// <summary>
    /// Default bezel/overlay artwork for games on this system — fills the space
    /// around a game's picture when it doesn't fill the whole screen (e.g. a 4:3
    /// game on a 16:9 display). Applies system-wide since this is really an
    /// aspect-ratio/cabinet-art concept rather than an emulator-specific one — the
    /// same system might be played through more than one emulator but usually wants
    /// the same bezel regardless. A specific game can override this via
    /// Game.BezelOverridePath for the rare exception. Empty means no bezel.
    /// </summary>
    public string BezelPath { get; set; } = string.Empty;

    /// <summary>
    /// Default display resolution/refresh rate for games on this system — many
    /// emulators and lightgun games only behave correctly at one specific mode.
    /// Applies system-wide, same "system default, game overrides" convention as
    /// BezelPath. A specific game can override this via Game.DisplayModeOverride.
    /// Null means "don't change the display mode for this system's games."
    /// </summary>
    public DisplayMode? DisplayMode { get; set; }
}
