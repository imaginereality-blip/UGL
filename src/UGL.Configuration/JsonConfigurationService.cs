using System.Text.Json;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Configuration;

internal sealed class JsonConfigurationService : IConfigurationService
{
    private readonly ILogger<JsonConfigurationService> _logger;
    private readonly string _configRoot;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private AppSettings _settings = new();
    private List<Category> _categories = [];
    private List<GameSystem> _systems = [];

    public AppSettings Settings => _settings;

    public JsonConfigurationService(ILogger<JsonConfigurationService> logger)
    {
        _logger = logger;
        _configRoot = Path.Combine(AppContext.BaseDirectory, "config");
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Loading configuration from: {ConfigRoot}", _configRoot);

        if (!Directory.Exists(_configRoot))
        {
            _logger.LogWarning("Config directory not found at {ConfigRoot}. Using defaults.", _configRoot);
            return;
        }

        _settings   = await LoadJsonAsync<AppSettings>("settings.json", ct) ?? new AppSettings();
        _categories = await LoadJsonAsync<List<Category>>("categories.json", ct) ?? [];
        _systems    = await LoadJsonAsync<List<GameSystem>>("systems.json", ct) ?? [];

        _logger.LogInformation(
            "Configuration loaded: {CategoryCount} categories, {SystemCount} systems.",
            _categories.Count, _systems.Count);
    }

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Category>>(_categories);

    public Task<IReadOnlyList<GameSystem>> GetSystemsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GameSystem>>(_systems);

    public async Task AddOrUpdateSystemAsync(GameSystem system, CancellationToken ct = default)
    {
        var idx = _systems.FindIndex(s => string.Equals(s.Id, system.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) _systems[idx] = system;
        else _systems.Add(system);
        await SaveSystemsAsync(ct);
    }

    public async Task DeleteSystemAsync(string id, CancellationToken ct = default)
    {
        _systems.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        await SaveSystemsAsync(ct);
    }

    public async Task UpdateCategoriesAsync(IEnumerable<Category> categories, CancellationToken ct = default)
    {
        _categories = categories.ToList();
        var json = JsonSerializer.Serialize(_categories, JsonOptions);
        await File.WriteAllTextAsync(ConfigPath("categories.json"), json, ct);
        _logger.LogInformation("categories.json saved ({Count} categories).", _categories.Count);
    }

    public async Task UpdateSettingsAsync(AppSettings settings, CancellationToken ct = default)
    {
        _settings = settings;
        await SaveSettingsAsync(ct);
    }

    public async Task SaveSettingsAsync(CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        await File.WriteAllTextAsync(ConfigPath("settings.json"), json, ct);
        _logger.LogInformation("Settings saved.");
    }

    private async Task SaveSystemsAsync(CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(_systems, JsonOptions);
        await File.WriteAllTextAsync(ConfigPath("systems.json"), json, ct);
        _logger.LogInformation("systems.json saved ({Count} systems).", _systems.Count);
    }

    private async Task<T?> LoadJsonAsync<T>(string fileName, CancellationToken ct)
    {
        var path = ConfigPath(fileName);
        if (!File.Exists(path)) { _logger.LogWarning("Config file not found: {Path}", path); return default; }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }

    private string ConfigPath(string fileName) => Path.Combine(_configRoot, fileName);
}
