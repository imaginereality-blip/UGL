namespace UGL.Core.Models;

/// <summary>
/// Represents one emulator definition loaded from emulators.json.
/// When IsRetroArchCore is true, ExecutablePath targets a .dll core library
/// and RetroArch contains the execution parameters.
/// </summary>
public sealed class Emulator
{
    public required string Id   { get; set; }
    public required string Name { get; set; }

    /// <summary>
    /// When false (default): direct executable launcher.
    ///   ExecutablePath = path to .exe
    ///   Arguments      = CLI args with {rom} token
    ///
    /// When true: RetroArch puppeteer mode.
    ///   ExecutablePath = path to .dll core library
    ///   Arguments      = ignored (built by RetroArchConfigGenerator)
    ///   RetroArch      = execution + optimisation parameters
    /// </summary>
    public bool IsRetroArchCore { get; set; } = false;

    /// <summary>
    /// When true: direct-launch mode, for native Windows games and launcher-protocol
    /// titles (Steam, Epic, GOG Galaxy) that have no separate "emulator" — the game
    /// itself is what gets launched.
    ///   ExecutablePath = ignored.
    ///   Game.RomPath   = the actual target: either an absolute path to the game's own
    ///                    .exe, or a launcher protocol URI (e.g. "steam://rungameid/12345").
    ///   Arguments      = literal CLI args passed as-is (no {rom} token — the target
    ///                    itself already comes from Game.RomPath).
    /// Mutually exclusive with IsRetroArchCore in practice (checked in that order by
    /// the launcher; not separately validated on the model itself, same convention as
    /// AudioPlaylist.IsGlobal's exclusivity — see SDS §9.6).
    /// </summary>
    public bool IsDirectLaunch { get; set; } = false;

    /// <summary>Exe path (standard) or .dll core path (RetroArch mode). Unused in direct-launch mode.</summary>
    public string ExecutablePath    { get; set; } = string.Empty;

    /// <summary>CLI arguments with {rom} token. Unused in RetroArch mode; literal (no token) in direct-launch mode.</summary>
    public string Arguments         { get; set; } = string.Empty;

    public string SupportedSystems  { get; set; } = string.Empty;

    /// <summary>
    /// BIOS file(s) this emulator/core needs, applying to every game launched
    /// through it — the common case. A specific game can override this instead via
    /// Game.BiosOverridePaths for the rare exception (e.g. a region-specific variant).
    /// Stored relative to the app's own bios\ folder when possible.
    /// </summary>
    public List<string> BiosPaths { get; set; } = [];

    /// <summary>Free-text notes and future per-emulator settings.</summary>
    public string Notes             { get; set; } = string.Empty;

    /// <summary>RetroArch-specific settings. Non-null when IsRetroArchCore is true.</summary>
    public RetroArchConfig? RetroArch { get; set; }
}
