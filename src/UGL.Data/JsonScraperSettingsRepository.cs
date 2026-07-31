using System.Text.Json;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Data;

/// <summary>
/// Reads and writes scraper/ComfyUI configuration from {exeDir}/config/scraper.json —
/// same pattern as JsonHookSettingsRepository.
/// </summary>
public sealed class JsonScraperSettingsRepository : IScraperSettingsRepository
{
    private readonly ILogger<JsonScraperSettingsRepository> _logger;
    private readonly string _scraperJsonPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private ScraperSettings? _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonScraperSettingsRepository(ILogger<JsonScraperSettingsRepository> logger)
    {
        _logger = logger;
        _scraperJsonPath = Path.Combine(AppContext.BaseDirectory, "config", "scraper.json");
    }

    public async Task<ScraperSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _settings!;
    }

    public async Task SaveSettingsAsync(ScraperSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _settings = settings;
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var configDir = Path.GetDirectoryName(_scraperJsonPath);
            if (configDir is not null) Directory.CreateDirectory(configDir);
            await File.WriteAllTextAsync(_scraperJsonPath, json, ct);
            _logger.LogInformation("scraper.json saved (preferred source={Source}).", settings.PreferredSource);
        }
        finally { _lock.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_settings is not null) return;
        await _lock.WaitAsync(ct);
        try
        {
            if (_settings is not null) return;
            if (!File.Exists(_scraperJsonPath))
            {
                _logger.LogInformation("scraper.json not found at {Path}. Using defaults.", _scraperJsonPath);
                _settings = new ScraperSettings();
                return;
            }

            await using var stream = File.OpenRead(_scraperJsonPath);
            _settings = await JsonSerializer.DeserializeAsync<ScraperSettings>(stream, JsonOptions, ct) ?? new ScraperSettings();
            _logger.LogInformation("scraper.json loaded (preferred source={Source}).", _settings.PreferredSource);
        }
        finally { _lock.Release(); }
    }
}
