using System.Text.Json;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Scraping;

/// <summary>
/// TheGamesDB — verified endpoint shapes against current community references (the
/// official docs require an already-issued key to browse interactively).
///   Search:  GET https://api.thegamesdb.net/v1/Games/ByGameName?apikey=..&amp;name=..
///            -> { data: { games: [ { id, game_title, players, ... } ] } }
///   Images:  GET https://api.thegamesdb.net/v1/Games/Images?apikey=..&amp;games_id=..
///            -> { data: { base_url: { original, thumb, ... }, images: { "{id}": [ {type,filename}, ... ] } } }
///            type "boxart" = cover; final URL = base_url.original + filename.
/// Parsed defensively (JsonDocument) for the same reason as ScreenScraperSource.
/// </summary>
public sealed class TheGamesDbScraperSource : IGameScraperSource
{
    private const string BaseUrl = "https://api.thegamesdb.net/v1";
    private readonly HttpClient _http;
    private readonly IScraperSettingsRepository _settingsRepo;
    private readonly ILogger<TheGamesDbScraperSource> _logger;

    public ScraperSourceType SourceType => ScraperSourceType.TheGamesDb;

    public TheGamesDbScraperSource(HttpClient http, IScraperSettingsRepository settingsRepo, ILogger<TheGamesDbScraperSource> logger)
    {
        _http = http;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    // platformHint accepted for interface parity but not yet applied — same reasoning
    // as ScreenScraperSource (no UGL system → TheGamesDB platform-id mapping yet).
    public async Task<IReadOnlyList<ScraperSearchResult>> SearchAsync(string title, string? platformHint, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.TheGamesDbApiKey)) return [];

        var url = $"{BaseUrl}/Games/ByGameName?apikey={Uri.EscapeDataString(settings.TheGamesDbApiKey)}&name={Uri.EscapeDataString(title)}";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TheGamesDB search failed: {Status}", response.StatusCode);
                return [];
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            if (!doc.RootElement.TryGetProperty("data", out var dataEl) ||
                !dataEl.TryGetProperty("games", out var gamesEl) ||
                gamesEl.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<ScraperSearchResult>();
            foreach (var g in gamesEl.EnumerateArray())
            {
                if (!g.TryGetProperty("id", out var idEl)) continue;
                var releaseYear = g.TryGetProperty("release_date", out var relEl) && relEl.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(relEl.GetString(), out var dt) ? dt.Year : (int?)null;

                results.Add(new ScraperSearchResult
                {
                    SourceGameId = idEl.ToString(),
                    Title = g.TryGetProperty("game_title", out var titleEl) ? titleEl.GetString() ?? title : title,
                    ReleaseYear = releaseYear,
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TheGamesDB search request failed.");
            return [];
        }
    }

    public async Task<ScraperGameMetadata?> GetDetailsAsync(string sourceGameId, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.TheGamesDbApiKey)) return null;

        var gameUrl = $"{BaseUrl}/Games/ByGameID?apikey={Uri.EscapeDataString(settings.TheGamesDbApiKey)}&id={Uri.EscapeDataString(sourceGameId)}&fields=players,genres,overview";
        var imagesUrl = $"{BaseUrl}/Games/Images?apikey={Uri.EscapeDataString(settings.TheGamesDbApiKey)}&games_id={Uri.EscapeDataString(sourceGameId)}";

        try
        {
            string title = string.Empty, description = string.Empty;
            int? players = null;

            using (var response = await _http.GetAsync(gameUrl, ct))
            {
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
                    if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                        dataEl.TryGetProperty("games", out var gamesEl) &&
                        gamesEl.ValueKind == JsonValueKind.Array)
                    {
                        var g = gamesEl.EnumerateArray().FirstOrDefault();
                        if (g.ValueKind == JsonValueKind.Object)
                        {
                            title = g.TryGetProperty("game_title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                            description = g.TryGetProperty("overview", out var o) ? o.GetString() ?? string.Empty : string.Empty;
                            if (g.TryGetProperty("players", out var p) && p.ValueKind == JsonValueKind.Number)
                                players = p.GetInt32();
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("TheGamesDB details fetch failed: {Status}", response.StatusCode);
                }
            }

            string? cover = null;
            using (var response = await _http.GetAsync(imagesUrl, ct))
            {
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
                    if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                        dataEl.TryGetProperty("base_url", out var baseUrlEl) &&
                        baseUrlEl.TryGetProperty("original", out var originalEl) &&
                        dataEl.TryGetProperty("images", out var imagesEl) &&
                        imagesEl.TryGetProperty(sourceGameId, out var gameImagesEl) &&
                        gameImagesEl.ValueKind == JsonValueKind.Array)
                    {
                        var baseUrl = originalEl.GetString();
                        foreach (var img in gameImagesEl.EnumerateArray())
                        {
                            var type = img.TryGetProperty("type", out var t2) ? t2.GetString() : null;
                            var filename = img.TryGetProperty("filename", out var f) ? f.GetString() : null;
                            if (type == "boxart" && filename is not null && baseUrl is not null)
                            {
                                cover = baseUrl + filename;
                                break;
                            }
                        }
                    }
                }
            }

            return new ScraperGameMetadata
            {
                Title = title,
                Players = players,
                Description = description,
                CoverImageUrl = cover,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TheGamesDB details request failed.");
            return null;
        }
    }
}
