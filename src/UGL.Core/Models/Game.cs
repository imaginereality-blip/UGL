namespace UGL.Core.Models;

/// <summary>
/// Represents a single launchable game entry. All fields are data-driven
/// from games.json and are never hard-coded in application logic.
/// </summary>
public sealed class Game
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string SystemId { get; set; }

    /// <summary>
    /// A game can belong to any number of categories — CategoryIds, not a single
    /// CategoryId, so it can show up when browsing any of them from the Home Menu.
    /// </summary>
    public List<string> CategoryIds { get; set; } = [];

    /// <summary>
    /// The ROM file path for emulated games. For an Emulator with IsDirectLaunch=true,
    /// this field doubles as the direct-launch target instead: an absolute path to a
    /// native Windows game's own .exe, or a launcher protocol URI (e.g.
    /// "steam://rungameid/12345", "com.epicgames.launcher://apps/...").
    /// </summary>
    public required string RomPath { get; set; }
    public required string EmulatorId { get; set; }

    /// <summary>
    /// Only meaningful for a direct-launch Emulator (Steam/Epic/GOG Galaxy). Some
    /// launcher protocols hand off to the actual game process and the process UGL
    /// spawns (the launcher/protocol handler) exits almost immediately — tracking
    /// that process's exit would return focus to UGL while the real game is still
    /// running. When set, ProcessEmulatorLauncher instead waits for a new process
    /// with this name to appear after launch and tracks *that* process's exit as the
    /// "game finished" signal. Leave blank for a plain .exe game, where the launched
    /// process already *is* the game.
    /// </summary>
    public string ProcessNameOverride { get; set; } = string.Empty;
    public GameMedia Media { get; set; } = new();
    public int Players { get; set; } = 1;
    public string Genre { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }

    /// <summary>
    /// Overrides the emulator's own configured BIOS file(s) for this specific game
    /// only — nearly always empty. Most games use whatever BIOS their Emulator is
    /// configured with (Emulator.BiosPaths); this exists only for the rare game that
    /// needs something different (e.g. a region-specific BIOS variant).
    /// </summary>
    public List<string> BiosOverridePaths { get; set; } = [];

    /// <summary>
    /// Overrides the system's own default bezel/overlay art for this specific game
    /// only — nearly always empty. Most games use whatever bezel their System is
    /// configured with; this exists for games that need a game-specific bezel
    /// instead (e.g. matching that specific game's own cabinet artwork).
    /// </summary>
    public string BezelOverridePath { get; set; } = string.Empty;

    /// <summary>
    /// Overrides the system's own default display mode for this specific game only —
    /// nearly always null. Most games use whatever mode their System is configured
    /// with (GameSystem.DisplayMode); this exists for a game that needs something
    /// different (e.g. a lightgun game that only behaves correctly at 640x480).
    /// </summary>
    public DisplayMode? DisplayModeOverride { get; set; }

    /// <summary>
    /// DemulShooter's per-title "-target=" argument (e.g. "hotd2" for House of the
    /// Dead 2) — required for DemulShooter to correctly translate lightgun aiming for
    /// this specific game. Empty means DemulShooter is not launched for this game,
    /// even if HookSettings.DemulShooterEnabled is true globally.
    /// </summary>
    public string DemulShooterTarget { get; set; } = string.Empty;

    /// <summary>
    /// Peripheral types to silently ignore input from while this game is running —
    /// e.g. disabling Lightgun and Wheel for a fighting game so they can't
    /// interfere. Empty by default (nothing disabled). Applies only to RawInput
    /// peripherals (lightguns, wheels, spinners, trackballs); the primary XInput
    /// controller used for menu navigation is never affected by this.
    /// </summary>
    public List<RawInputDeviceType> DisabledDeviceTypes { get; set; } = [];

    /// <summary>
    /// Richer alternative to DisabledDeviceTypes for a specific game: explicit,
    /// ranked, per-player-slot device preferences (e.g. Player 1 = this specific
    /// wheel, falling back to a specific gamepad if the wheel isn't connected).
    /// When non-empty, this takes over peripheral visibility for the game entirely —
    /// DisabledDeviceTypes is ignored for it, since the assignment list already
    /// implies exactly which devices should stay visible. Empty in the overwhelming
    /// majority of cases (DisabledDeviceTypes alone covers most needs).
    /// </summary>
    public List<PlayerDeviceAssignment> PlayerDeviceAssignments { get; set; } = [];
}
