using System.Text.Json;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Data;

/// <summary>
/// Reads and writes the single hook-tool configuration from
/// {exeDir}/config/hooks.json.
/// </summary>
public sealed class JsonHookSettingsRepository : IHookSettingsRepository
{
    private readonly ILogger<JsonHookSettingsRepository> _logger;
    private readonly string _hooksJsonPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private HookSettings? _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonHookSettingsRepository(ILogger<JsonHookSettingsRepository> logger)
    {
        _logger = logger;
        _hooksJsonPath = Path.Combine(AppContext.BaseDirectory, "config", "hooks.json");
    }

    public async Task<HookSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _settings!;
    }

    public async Task SaveSettingsAsync(HookSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _settings = settings;
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var configDir = Path.GetDirectoryName(_hooksJsonPath);
            if (configDir is not null) Directory.CreateDirectory(configDir);
            await File.WriteAllTextAsync(_hooksJsonPath, json, ct);
            _logger.LogInformation("hooks.json saved (tool={Tool}, enabled={Enabled}).",
                settings.ToolType, settings.EnabledGlobally);
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
            if (!File.Exists(_hooksJsonPath))
            {
                _logger.LogInformation("hooks.json not found at {Path}. Using defaults (disabled).", _hooksJsonPath);
                _settings = new HookSettings();
                return;
            }

            await using var stream = File.OpenRead(_hooksJsonPath);
            _settings = await JsonSerializer.DeserializeAsync<HookSettings>(stream, JsonOptions, ct) ?? new HookSettings();
            _logger.LogInformation("hooks.json loaded (tool={Tool}, enabled={Enabled}).",
                _settings.ToolType, _settings.EnabledGlobally);
        }
        finally { _lock.Release(); }
    }
}
