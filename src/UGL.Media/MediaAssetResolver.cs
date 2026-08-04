using UGL.Core.Models;

namespace UGL.Media;

/// <summary>
/// Resolves absolute file system paths for all media asset types.
///
/// Games: use {systemId}-{gameId} slug convention (set via Config Editor,
/// stored on GameMedia). The resolver checks the stored path first and falls
/// back to slug-based lookup for backward compatibility.
///
/// Categories: use the path stored directly on Category.ArtPath /
/// Category.VideoPath — no filename convention required. Any Windows-
/// compatible filename works.
/// </summary>
public sealed class MediaAssetResolver
{
    private readonly string _mediaRoot;

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".avi"];

    public MediaAssetResolver(string mediaRootPath)
    {
        _mediaRoot = Path.IsPathRooted(mediaRootPath)
            ? mediaRootPath
            : Path.Combine(AppContext.BaseDirectory, mediaRootPath);
    }

    // ── Game assets (slug-based, set via Config Editor) ───────────────────

    public string? ResolveCover(Game game)
    {
        if (game.Media.PreferCardArtAsCover && ResolveCardArt(game) is { } cardArt)
            return cardArt;
        if (!string.IsNullOrWhiteSpace(game.Media.CoverPath))
            return ResolvePath(game.Media.CoverPath);
        return FindImage("covers", Slug(game));
    }

    public string? ResolveCardArt(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.Media.CardArtPath))
            return ResolvePath(game.Media.CardArtPath);
        return FindImage("cardart", Slug(game));
    }

    public string? ResolveBackground(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.Media.BackgroundPath))
            return ResolvePath(game.Media.BackgroundPath);
        return FindImage("backgrounds", Slug(game));
    }

    public string? ResolveLogo(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.Media.LogoPath))
            return ResolvePath(game.Media.LogoPath);
        return FindImage("logos", Slug(game));
    }

    public string? ResolveScreenshot(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.Media.ScreenshotPath))
            return ResolvePath(game.Media.ScreenshotPath);
        return FindImage("screenshots", Slug(game));
    }

    /// <summary>Up to 3 persisted screenshots (see GameMedia.ScreenshotPaths) — used
    /// as ComfyUI poster-collage reference material. Distinct from the single-file
    /// ResolveScreenshot above.</summary>
    public List<string> ResolveScreenshots(Game game)
    {
        if (game.Media.ScreenshotPaths.Count > 0)
            return game.Media.ScreenshotPaths
                .Select(ResolvePath)
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();

        var slug = Slug(game);
        var found = new List<string>();
        for (int i = 1; i <= 3; i++)
        {
            if (FindImage("screenshots", $"{slug}-{i}") is { } path) found.Add(path);
        }
        return found;
    }

    public string? ResolveMarquee(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.Media.MarqueePath))
            return ResolvePath(game.Media.MarqueePath);
        return FindImage("marquees", Slug(game));
    }

    public string? ResolveVideo(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.Media.VideoPath))
            return ResolvePath(game.Media.VideoPath);
        return FindVideo("video", Slug(game));
    }

    // ── Category assets (path stored directly on Category model) ─────────

    /// <summary>
    /// Returns the absolute path to the category card art.
    /// Uses Category.ArtPath directly — no naming convention enforced.
    /// Returns null if ArtPath is empty or the file does not exist.
    /// </summary>
    public string? ResolveCategoryArt(Category category)
    {
        if (string.IsNullOrWhiteSpace(category.ArtPath)) return null;
        return ResolvePath(category.ArtPath);
    }

    /// <summary>
    /// Returns the absolute path to the category video preview.
    /// Uses Category.VideoPath directly — no naming convention enforced.
    /// Returns null if VideoPath is empty or the file does not exist.
    /// </summary>
    public string? ResolveCategoryVideo(Category category)
    {
        if (string.IsNullOrWhiteSpace(category.VideoPath)) return null;
        return ResolvePath(category.VideoPath);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string Slug(Game game) =>
        $"{game.SystemId}-{game.Id}".ToLowerInvariant();

    private string? FindImage(string subfolder, string slug)
    {
        foreach (var ext in ImageExtensions)
        {
            var path = Path.Combine(_mediaRoot, subfolder, slug + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private string? FindVideo(string subfolder, string slug)
    {
        foreach (var ext in VideoExtensions)
        {
            var path = Path.Combine(_mediaRoot, subfolder, slug + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>
    /// Resolves a stored path to an absolute path.
    /// Relative paths are resolved against the exe directory.
    /// Returns null if the file does not exist.
    /// </summary>
    private static string? ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var absolute = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        return File.Exists(absolute) ? absolute : null;
    }
}
