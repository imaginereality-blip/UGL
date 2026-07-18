namespace UGL.Core.Models;

/// <summary>
/// Top-level application settings persisted to config/settings.json.
///
/// Root paths are relative to the executable directory by default,
/// so the entire UltimateGameLauncher folder is portable.
/// Users can override any path to an absolute location via the
/// Settings → Paths tab.
/// </summary>
public sealed class AppSettings
{
    // ── Root Paths ─────────────────────────────────────────────────────────

    /// <summary>Root folder for all media assets (art, video, sounds, music).</summary>
    public string MediaRootPath     { get; set; } = "media";

    /// <summary>Root folder for ROM files, organised by system sub-folder.</summary>
    public string RomsRootPath      { get; set; } = "roms";

    /// <summary>Root folder for emulator installations.</summary>
    public string EmulatorsRootPath { get; set; } = "emulators";

    /// <summary>Root folder for add-ons (MameHooker, HookReaper, etc.).</summary>
    public string AddonsRootPath    { get; set; } = "addons";

    /// <summary>Root folder for application log files.</summary>
    public string LogsRootPath      { get; set; } = "logs";

    // ── Active configuration ────────────────────────────────────────────────
    public string ActiveThemeId     { get; set; } = "default";
    public string DefaultCategoryId { get; set; } = string.Empty;
    public string Language          { get; set; } = "en-US";
    public int    TargetFrameRate   { get; set; } = 60;

    // ── Audio ──────────────────────────────────────────────────────────────
    public bool  EnableBackgroundMusic   { get; set; } = true;
    public bool  EnableNavigationSounds  { get; set; } = true;
    public float MusicVolume             { get; set; } = 0.5f;
    public float SoundVolume             { get; set; } = 1.0f;

    /// <summary>Paths relative to exe or absolute. Any filename is valid.</summary>
    public string SoundNavigatePath { get; set; } = "media/sounds/navigate.wav";
    public string SoundConfirmPath  { get; set; } = "media/sounds/confirm.wav";
    public string SoundBackPath     { get; set; } = "media/sounds/back.wav";
    public string SoundErrorPath    { get; set; } = "media/sounds/error.wav";

    // ── Video preview ──────────────────────────────────────────────────────
    public bool  EnableVideoPreview  { get; set; } = true;
    public int   VideoPreviewDelayMs { get; set; } = 0;
    public bool  VideoPreviewAudio   { get; set; } = false;
    public float VideoPreviewVolume  { get; set; } = 0.5f;

    // ── Card selection highlight ────────────────────────────────────────────
    // Applies to the selected card border in both the Home Menu (categories) and
    // Game Browser (games) — one shared appearance, configured in Settings → Theme.
    public string CardHighlightColor     { get; set; } = "#FFFFD700";
    public double CardHighlightIntensity { get; set; } = 1.0; // 0.0-1.0, applied as border opacity
    public string CardHighlightStyle     { get; set; } = "Solid"; // "Solid" | "Pulsing"
    public int    CardHighlightThickness { get; set; } = 4; // pixels, 2-5
}
