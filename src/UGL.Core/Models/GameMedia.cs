namespace UGL.Core.Models;

/// <summary>
/// Holds file paths for all media assets associated with a game.
/// All paths are relative to the configured media root directory.
/// A null or empty string means the asset is absent; the UI
/// falls back to a theme-defined placeholder.
/// </summary>
public sealed class GameMedia
{
    public string CoverPath { get; init; } = string.Empty;
    public string ScreenshotPath { get; init; } = string.Empty;
    public string VideoPath { get; init; } = string.Empty;
    public string LogoPath { get; init; } = string.Empty;
    public string MarqueePath { get; init; } = string.Empty;
    public string WheelArtPath { get; init; } = string.Empty;
    public string BoxArtPath { get; init; } = string.Empty;
    public string BackgroundPath { get; init; } = string.Empty;

    /// <summary>AI-generated poster/collage card art — a separate asset from
    /// CoverPath, stored in its own media subfolder.</summary>
    public string CardArtPath { get; init; } = string.Empty;

    /// <summary>When true and CardArtPath resolves to a real file, the browse grid
    /// displays Card Art instead of Cover Art for this game.</summary>
    public bool PreferCardArtAsCover { get; init; }

    /// <summary>Up to 3 scraped screenshots, persisted as real files (unlike the
    /// unused singular ScreenshotPath above) so the ComfyUI poster-collage generator
    /// can reuse them without re-scraping every session.</summary>
    public List<string> ScreenshotPaths { get; init; } = [];
}
