namespace UGL.Core.Models;

/// <summary>Which metadata/image source a scrape request should use.</summary>
public enum ScraperSourceType
{
    Igdb,
    ScreenScraper,
    TheGamesDb,
}

/// <summary>
/// Persisted to config/scraper.json. Credentials for all three supported sources are
/// kept side by side (not mutually exclusive) — the user picks a PreferredSource as
/// the default, but nothing stops configuring more than one and choosing per-search.
/// </summary>
public sealed class ScraperSettings
{
    public ScraperSourceType PreferredSource { get; set; } = ScraperSourceType.Igdb;

    // ── IGDB (id.twitch.tv OAuth2 client-credentials + api.igdb.com) ─────────────
    // Self-service: create a Twitch Developer app at dev.twitch.tv (free).
    public string IgdbClientId     { get; set; } = string.Empty;
    public string IgdbClientSecret { get; set; } = string.Empty;

    // ── ScreenScraper.fr ───────────────────────────────────────────────────────
    // ssid/sspassword = your personal ScreenScraper account. devid/devpassword =
    // credentials for a *registered application* — unlike IGDB/TheGamesDB this isn't
    // fully self-service; ScreenScraper requires registering "UltimateGameLauncher"
    // as a piece of software with their team before these are issued. Leave blank
    // (source unusable) until that's done.
    public string ScreenScraperUsername   { get; set; } = string.Empty;
    public string ScreenScraperPassword   { get; set; } = string.Empty;
    public string ScreenScraperDevId      { get; set; } = string.Empty;
    public string ScreenScraperDevPassword { get; set; } = string.Empty;

    // ── TheGamesDB ─────────────────────────────────────────────────────────────
    // Self-service: request a free key at forums.thegamesdb.net.
    public string TheGamesDbApiKey { get; set; } = string.Empty;

    // ── ComfyUI (game card art generation) ────────────────────────────────────
    /// <summary>Base URL of a running ComfyUI server, e.g. http://127.0.0.1:8188.</summary>
    public string ComfyUiEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Path to a workflow JSON file exported from ComfyUI's own UI ("Save (API
    /// Format)"). UGL doesn't generate a workflow itself — the actual checkpoint/
    /// nodes/settings are entirely up to whatever the user has installed. UGL
    /// substitutes the literal token "{{PROMPT}}" (must appear in exactly one node's
    /// text input, e.g. a CLIPTextEncode "text" field) with a prompt built from the
    /// game's title/genre before submitting. Used by the "Generate Poster Collage"
    /// action (multi-image collage from cover + screenshots).
    /// </summary>
    public string ComfyUiWorkflowPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to a second, separate workflow JSON used only by the "Clean Cover for
    /// Card" action: a single-image img2img pass that erases baked-in logo/text from
    /// the scraped cover and reconstructs the artwork underneath, before UGL composites
    /// the real (un-regenerated) logo back on top. Deliberately not the same workflow
    /// file as ComfyUiWorkflowPath — that one stitches up to 3 images into a collage,
    /// which is the wrong shape of graph for cleaning up a single cover. Optional: if
    /// left blank, the action falls back to a plain local resize with no cleanup.
    /// </summary>
    public string ComfyUiCleanupWorkflowPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to a PP-OCRv3 "DB" text-detection ONNX model (e.g.
    /// text_detection_en_ppocrv3_2023may.onnx from github.com/opencv/opencv_zoo) used
    /// by "Clean Cover for Card" to find stray logo/text regions anywhere on the cover
    /// — not just the main title logo, which is located separately via template
    /// matching against the game's own scraped logo asset (LogoRegionDetector). Optional:
    /// if left blank, that detection pass is skipped and the mask falls back to the
    /// main-logo match alone, or a fixed top-band guess if that also finds nothing.
    /// </summary>
    public string TextDetectionModelPath { get; set; } = string.Empty;
}

/// <summary>One candidate match from a scraper search — enough to let the user (or,
/// today, an auto-pick-first-result flow) identify the right game before fetching
/// full details.</summary>
public sealed class ScraperSearchResult
{
    public required string SourceGameId { get; init; }
    public required string Title { get; init; }
    public string Platform { get; init; } = string.Empty;
    public int? ReleaseYear { get; init; }
}

/// <summary>Full metadata + image URLs for one matched game, ready to apply to a
/// Game/GameMedia record.</summary>
public sealed class ScraperGameMetadata
{
    public required string Title { get; init; }
    public string Genre { get; init; } = string.Empty;
    public int? Players { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? CoverImageUrl { get; init; }
    public string? LogoImageUrl { get; init; }
    public string? MarqueeImageUrl { get; init; }

    /// <summary>All screenshots the source returned (not just the first) — used as
    /// IP-Adapter reference images for card-art generation.</summary>
    public List<string> ScreenshotImageUrls { get; init; } = [];

    /// <summary>Alternate box art / key art / fan art beyond the primary cover — also
    /// used as IP-Adapter reference images for card-art generation.</summary>
    public List<string> ArtworkImageUrls { get; init; } = [];
}
