using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Configuration;

internal sealed class NullConfigurationService : IConfigurationService
{
    private AppSettings _settings = new();
    public AppSettings Settings => _settings;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>());

    public Task<IReadOnlyList<GameSystem>> GetSystemsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<GameSystem>>(Array.Empty<GameSystem>());

    public Task AddOrUpdateSystemAsync(GameSystem system, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DeleteSystemAsync(string id, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task UpdateCategoriesAsync(IEnumerable<Category> categories, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        return Task.CompletedTask;
    }

    public Task SaveSettingsAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
