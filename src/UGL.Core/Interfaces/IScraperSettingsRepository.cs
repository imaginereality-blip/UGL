using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>Persists ScraperSettings to config/scraper.json — same pattern as
/// IHookSettingsRepository.</summary>
public interface IScraperSettingsRepository
{
    Task<ScraperSettings> GetSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(ScraperSettings settings, CancellationToken ct = default);
}
