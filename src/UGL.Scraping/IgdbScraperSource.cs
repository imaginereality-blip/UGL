using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Scraping;

/// <summary>
/// IGDB (powered by Twitch) — verified against IGDB's own docs and current
/// community references, not assumed from memory:
///   Auth:  POST https://id.twitch.tv/oauth2/token?client_id=..&amp;client_secret=..&amp;grant_type=client_credentials
///          -> { access_token, expires_in, token_type }, used as "Authorization: Bearer {token}"
///          plus a "Client-ID: {client_id}" header on every subsequent request.
///   Query: POST https://api.igdb.com/v4/games, body is Apicalypse text
///          ("fields ...; search "..."; limit ...;"), not a JSON body.
///   Images: https://images.igdb.com/igdb/image/upload/t_{size}/{image_id}.jpg
///           (t_cover_big = 227x320, used here for card art).
/// </summary>
public sealed class IgdbScraperSource : IGameScraperSource
{
    private readonly HttpClient _http;
    private readonly IScraperSettingsRepository _settingsRepo;
    private readonly ILogger<IgdbScraperSource> _logger;

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public ScraperSourceType SourceType => ScraperSourceType.Igdb;

    public IgdbScraperSource(HttpClient http, IScraperSettingsRepository settingsRepo, ILogger<IgdbScraperSource> logger)
    {
        _http = http;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScraperSearchResult>> SearchAsync(string title, string? platformHint, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync(ct);
        if (!await TryAuthenticateAsync(settings, ct)) return [];

        // Filtered-by-platform first — a title shared across multiple platforms
        // (very common; the NFL Blitz 2001 GBA port vs. the Dreamcast original is
        // exactly this case) otherwise returns whichever version IGDB ranks highest,
        // with no guarantee it matches the system the game is actually being added
        // for. Falls back to an unfiltered search only if the filtered one comes back
        // empty (system name not recognized/typo'd, or genuinely not on IGDB for that
        // platform) rather than silently only ever trying one or the other.
        if (!string.IsNullOrWhiteSpace(platformHint))
        {
            var filtered = await SearchInternalAsync(settings, title, platformHint, ct);
            if (filtered.Count > 0) return filtered;
        }

        return await SearchInternalAsync(settings, title, null, ct);
    }

    private async Task<IReadOnlyList<ScraperSearchResult>> SearchInternalAsync(
        ScraperSettings settings, string title, string? platformHint, CancellationToken ct)
    {
        var escapedTitle = title.Replace("\"", "\\\"");
        var where = string.IsNullOrWhiteSpace(platformHint)
            ? string.Empty
            : $" where platforms.name ~ *\"{platformHint.Replace("\"", "\\\"")}\"*;";
        var body = $"fields name, platforms.name, first_release_date; search \"{escapedTitle}\";{where} limit 15;";

        try
        {
            using var request = BuildRequest(settings, "games", body);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("IGDB search failed: {Status}", response.StatusCode);
                return [];
            }

            var games = await response.Content.ReadFromJsonAsync<List<IgdbGame>>(JsonOptions, ct) ?? [];
            return games.Select(g => new ScraperSearchResult
            {
                SourceGameId = g.Id.ToString(),
                Title = g.Name ?? title,
                Platform = g.Platforms?.FirstOrDefault()?.Name ?? string.Empty,
                ReleaseYear = g.FirstReleaseDate is { } unixSeconds
                    ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).Year
                    : null,
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IGDB search request failed.");
            return [];
        }
    }

    public async Task<ScraperGameMetadata?> GetDetailsAsync(string sourceGameId, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync(ct);
        if (!await TryAuthenticateAsync(settings, ct)) return null;

        var body = $"fields name, summary, genres.name, cover.image_id, artworks.image_id, screenshots.image_id; where id = {sourceGameId};";

        try
        {
            using var request = BuildRequest(settings, "games", body);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("IGDB details fetch failed: {Status}", response.StatusCode);
                return null;
            }

            var games = await response.Content.ReadFromJsonAsync<List<IgdbGame>>(JsonOptions, ct) ?? [];
            var g = games.FirstOrDefault();
            if (g is null) return null;

            return new ScraperGameMetadata
            {
                Title = g.Name ?? string.Empty,
                Genre = string.Join(", ", g.Genres?.Select(x => x.Name) ?? []),
                Description = g.Summary ?? string.Empty,
                CoverImageUrl = g.Cover?.ImageId is { } coverId ? BuildImageUrl(coverId, "t_cover_big") : null,
                ScreenshotImageUrl = g.Screenshots?.FirstOrDefault()?.ImageId is { } shotId
                    ? BuildImageUrl(shotId, "t_screenshot_big") : null,
                MarqueeImageUrl = g.Artworks?.FirstOrDefault()?.ImageId is { } artId
                    ? BuildImageUrl(artId, "t_screenshot_huge") : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IGDB details request failed.");
            return null;
        }
    }

    private static string BuildImageUrl(string imageId, string sizeToken) =>
        $"https://images.igdb.com/igdb/image/upload/{sizeToken}/{imageId}.jpg";

    private HttpRequestMessage BuildRequest(ScraperSettings settings, string endpoint, string apicalypseBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.igdb.com/v4/{endpoint}")
        {
            Content = new StringContent(apicalypseBody, Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("Client-ID", settings.IgdbClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken);
        return request;
    }

    private async Task<bool> TryAuthenticateAsync(ScraperSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.IgdbClientId) || string.IsNullOrWhiteSpace(settings.IgdbClientSecret))
        {
            _logger.LogDebug("IGDB not configured (missing Client ID/Secret).");
            return false;
        }

        // Reuse the cached token until shortly before it actually expires.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt - TimeSpan.FromMinutes(5))
            return true;

        try
        {
            var tokenUrl = $"https://id.twitch.tv/oauth2/token?client_id={Uri.EscapeDataString(settings.IgdbClientId)}" +
                            $"&client_secret={Uri.EscapeDataString(settings.IgdbClientSecret)}&grant_type=client_credentials";
            using var response = await _http.PostAsync(tokenUrl, content: null, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("IGDB/Twitch token request failed: {Status}", response.StatusCode);
                return false;
            }

            var token = await response.Content.ReadFromJsonAsync<TwitchTokenResponse>(JsonOptions, ct);
            if (token?.AccessToken is null) return false;

            _cachedToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IGDB/Twitch authentication failed.");
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class TwitchTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public long ExpiresIn { get; set; }
    }

    private sealed class IgdbGame
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? Summary { get; set; }
        [JsonPropertyName("first_release_date")] public long? FirstReleaseDate { get; set; }
        public List<IgdbNamed>? Platforms { get; set; }
        public List<IgdbNamed>? Genres { get; set; }
        public IgdbImage? Cover { get; set; }
        public List<IgdbImage>? Artworks { get; set; }
        public List<IgdbImage>? Screenshots { get; set; }
    }

    private sealed class IgdbNamed { public string? Name { get; set; } }
    private sealed class IgdbImage { [JsonPropertyName("image_id")] public string? ImageId { get; set; } }
}
