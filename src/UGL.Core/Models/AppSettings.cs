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

    // ── Title graphics (category card title-text overlay) ──────────────────
    // Text-over-art overlay on category cards, showing the category's own Label —
    // for categories with no logo art of their own. Configured in Settings → Title
    // Graphics.
    public bool   TitleGraphicsEnabled   { get; set; } = false;

    // Scale/position replaced the old Top/Middle/Bottom placement buttons — a
    // continuous slider covers every position those three buttons could reach, and
    // then some, so the discrete placement concept was dropped rather than kept
    // alongside it. Scale is the ONLY width control now (CategoryCard no longer
    // reserves a fixed 90% column on top of it — that double-counted and pushed
    // content past the card's clipped edge), so the default is under 100% to leave
    // headroom before the user pushes it higher.
    public double TitleGraphicsScale     { get; set; } = 0.9;
    public double TitleGraphicsPositionX { get; set; } = 0.0; // -50..50, % of card width
    public double TitleGraphicsPositionY { get; set; } = 0.0; // -50..50, % of card height

    // 3D bake style — extruded block-letter look baked offscreen via WebView2/three.js
    // (see TitleGraphicsBaker). Defaults match racing-3d-title.html's reference scene.
    public string TitleGraphicsFillTopColor    { get; set; } = "#B8860B";
    public string TitleGraphicsFillMidColor    { get; set; } = "#E8C848";
    public string TitleGraphicsFillBottomColor { get; set; } = "#FFF3C4";
    public string TitleGraphicsBevelColor      { get; set; } = "#A855FF";
    public string TitleGraphicsOutlineColor    { get; set; } = "#C41E1E";
    // 1-3 accent point lights: Main is always on, Key1/Key2 switch on as the count
    // increases (see TitleGraphicsBaker's template.html for exact positions).
    public int    TitleGraphicsLightCount      { get; set; } = 2; // 1-3
    public string TitleGraphicsLightMainColor  { get; set; } = "#F5C518";
    public string TitleGraphicsLightKey1Color  { get; set; } = "#3D1466";
    public string TitleGraphicsLightKey2Color  { get; set; } = "#FFFFFF";

    public double TitleGraphicsRotationXDegrees { get; set; } = 0.0; // -45..45
    public double TitleGraphicsRotationYDegrees { get; set; } = 0.0; // -45..45
    public double TitleGraphicsRotationZDegrees { get; set; } = 0.0; // -15..15

    // ── Peripheral firewall (HidHide) ────────────────────────────────────────
    // Backs the real (OS-level) version of Game.DisabledDeviceTypes — without this,
    // "disabling" a peripheral only affects UGL's own RawInput event pipeline, not
    // what the actual emulator/game process can see (see ProcessEmulatorLauncher).
    public bool   HidHideEnabled  { get; set; } = false;
    public string HidHideCliPath  { get; set; } = string.Empty;
}
