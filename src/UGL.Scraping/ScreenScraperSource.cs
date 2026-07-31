using System.Text.Json;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Scraping;

/// <summary>
/// ScreenScraper.fr — verified endpoint shapes against community references
/// (Skyscraper, various open-source wrappers), NOT the official docs directly, since
/// full docs require an already-issued devid to view. Treat this implementation as
/// higher-risk than IGDB/TheGamesDB until exercised against real credentials — it
/// parses defensively (JsonDocument, tolerant of missing fields) rather than via
/// strict POCOs, so a shape mismatch degrades to "field not found" instead of a crash.
///
///   Auth: no separate token step — devid/devpassword/softname (registered
///         application credentials) and ssid/sspassword (personal account) are all
///         passed as query-string parameters on every request.
///   Search: GET https://api.screenscraper.fr/api2/jeuRecherche.php?recherche=...
///           -> { response: { jeux: [ {...}, ... ] } }
///   Each "jeu": id, noms[] ({region,text}), synopsis[] ({langue,text}),
///     genres[] (nested noms[]), joueurs ({text}), medias[] ({type,url,region}) with
///     types like "box-2D" (cover), "wheel"/"wheel-hd" (logo), "marquee" (marquee),
///     "ss" (screenshot).
/// </summary>
public sealed class ScreenScraperSource : IGameScraperSource
{
    private const string BaseUrl = "https://api.screenscraper.fr/api2";
    private readonly HttpClient _http;
    private readonly IScraperSettingsRepository _settingsRepo;
    private readonly ILogger<ScreenScraperSource> _logger;

    public ScraperSourceType SourceType => ScraperSourceType.ScreenScraper;

    public ScreenScraperSource(HttpClient http, IScraperSettingsRepository settingsRepo, ILogger<ScreenScraperSource> logger)
    {
        _http = http;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScraperSearchResult>> SearchAsync(string title, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync(ct);
        if (!IsConfigured(settings)) return [];

        var url = $"{BaseUrl}/jeuRecherche.php?{AuthQueryString(settings)}&output=json&recherche={Uri.EscapeDataString(title)}";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ScreenScraper search failed: {Status}", response.StatusCode);
                return [];
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            if (!doc.RootElement.TryGetProperty("response", out var respEl) ||
                !respEl.TryGetProperty("jeux", out var jeuxEl) ||
                jeuxEl.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<ScraperSearchResult>();
            foreach (var jeu in jeuxEl.EnumerateArray())
            {
                var id = GetString(jeu, "id");
                if (id is null) continue;
                results.Add(new ScraperSearchResult
                {
                    SourceGameId = id,
                    Title = ExtractBestName(jeu) ?? title,
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ScreenScraper search request failed.");
            return [];
        }
    }

    public async Task<ScraperGameMetadata?> GetDetailsAsync(string sourceGameId, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync(ct);
        if (!IsConfigured(settings)) return null;

        var url = $"{BaseUrl}/jeuInfos.php?{AuthQueryString(settings)}&output=json&gameid={Uri.EscapeDataString(sourceGameId)}";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ScreenScraper details fetch failed: {Status}", response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            if (!doc.RootElement.TryGetProperty("response", out var respEl) ||
                !respEl.TryGetProperty("jeu", out var jeu))
                return null;

            var genres = new List<string>();
            if (jeu.TryGetProperty("genres", out var genresEl) && genresEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var genre in genresEl.EnumerateArray())
                {
                    var name = genre.TryGetProperty("noms", out var namesEl) ? ExtractBestName(namesEl) : null;
                    if (name is not null) genres.Add(name);
                }
            }

            int? players = null;
            if (jeu.TryGetProperty("joueurs", out var joueursEl))
            {
                var text = joueursEl.ValueKind == JsonValueKind.Object ? GetString(joueursEl, "text") : joueursEl.GetString();
                if (text is not null)
                {
                    var digits = new string(text.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out var n) && n > 0) players = n;
                }
            }

            string? synopsis = null;
            if (jeu.TryGetProperty("synopsis", out var synopsisEl) && synopsisEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in synopsisEl.EnumerateArray())
                {
                    var lang = GetString(s, "langue");
                    var text = GetString(s, "text");
                    if (text is null) continue;
                    if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase)) { synopsis = text; break; }
                    synopsis ??= text;
                }
            }

            string? cover = null, logo = null, marquee = null, screenshot = null;
            if (jeu.TryGetProperty("medias", out var mediasEl) && mediasEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var media in mediasEl.EnumerateArray())
                {
                    var type = GetString(media, "type");
                    var url2 = GetString(media, "url");
                    if (type is null || url2 is null) continue;
                    switch (type)
                    {
                        case "box-2D": cover ??= url2; break;
                        case "wheel": case "wheel-hd": logo ??= url2; break;
                        case "marquee": marquee ??= url2; break;
                        case "ss": screenshot ??= url2; break;
                    }
                }
            }

            return new ScraperGameMetadata
            {
                Title = (jeu.TryGetProperty("noms", out var namesEl2) ? ExtractBestName(namesEl2) : null) ?? string.Empty,
                Genre = string.Join(", ", genres),
                Players = players,
                Description = synopsis ?? string.Empty,
                CoverImageUrl = cover,
                LogoImageUrl = logo,
                MarqueeImageUrl = marquee,
                ScreenshotImageUrl = screenshot,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ScreenScraper details request failed.");
            return null;
        }
    }

    private static bool IsConfigured(ScraperSettings s) =>
        !string.IsNullOrWhiteSpace(s.ScreenScraperUsername) && !string.IsNullOrWhiteSpace(s.ScreenScraperPassword) &&
        !string.IsNullOrWhiteSpace(s.ScreenScraperDevId) && !string.IsNullOrWhiteSpace(s.ScreenScraperDevPassword);

    private static string AuthQueryString(ScraperSettings s) =>
        $"devid={Uri.EscapeDataString(s.ScreenScraperDevId)}&devpassword={Uri.EscapeDataString(s.ScreenScraperDevPassword)}" +
        $"&softname=UltimateGameLauncher&ssid={Uri.EscapeDataString(s.ScreenScraperUsername)}&sspassword={Uri.EscapeDataString(s.ScreenScraperPassword)}";

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    /// <summary>ScreenScraper "noms" arrays hold one entry per region ({region,text}) —
    /// prefer "ss" (ScreenScraper's own canonical) or "wor" (world), else the first entry.</summary>
    private static string? ExtractBestName(JsonElement namesArray)
    {
        if (namesArray.ValueKind != JsonValueKind.Array) return null;
        string? first = null;
        foreach (var entry in namesArray.EnumerateArray())
        {
            var region = GetString(entry, "region");
            var text = GetString(entry, "text");
            if (text is null) continue;
            first ??= text;
            if (region is "ss" or "wor") return text;
        }
        return first;
    }
}
