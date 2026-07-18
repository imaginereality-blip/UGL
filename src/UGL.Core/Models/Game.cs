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

    public required string RomPath { get; set; }
    public required string EmulatorId { get; set; }
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
}
