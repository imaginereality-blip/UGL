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

    /// <summary>Exe path (standard) or .dll core path (RetroArch mode).</summary>
    public string ExecutablePath    { get; set; } = string.Empty;

    /// <summary>CLI arguments with {rom} token. Unused in RetroArch mode.</summary>
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
