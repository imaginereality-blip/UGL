using UGL.Core.Models;

namespace UGL.Core.Interfaces;

public interface IConfigurationService
{
    AppSettings Settings { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameSystem>> GetSystemsAsync(CancellationToken cancellationToken = default);
    Task AddOrUpdateSystemAsync(GameSystem system, CancellationToken cancellationToken = default);
    Task DeleteSystemAsync(string id, CancellationToken cancellationToken = default);
    Task UpdateCategoriesAsync(IEnumerable<Category> categories, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the in-memory settings with the provided object and
    /// persists to settings.json immediately.
    /// </summary>
    Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(CancellationToken cancellationToken = default);
}
