using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Data;

/// <summary>
/// Reads and writes the game catalog from {exeDir}/config/games.json.
/// </summary>
internal sealed class JsonGameRepository : IGameRepository
{
    private readonly ILogger<JsonGameRepository> _logger;
    private readonly string _gamesJsonPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private List<Game>? _games;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonGameRepository(ILogger<JsonGameRepository> logger)
    {
        _logger = logger;
        _gamesJsonPath = Path.Combine(AppContext.BaseDirectory, "config", "games.json");
    }

    public async Task<IReadOnlyList<Game>> GetAllGamesAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _games!.AsReadOnly();
    }

    /// <summary>Reserved category Id — membership is computed from Game.IsFavorite rather
    /// than Game.CategoryIds, so a game "joins" Favorites automatically when favorited
    /// rather than needing to be manually added to it like a normal category.</summary>
    private const string FavoritesCategoryId = "favorites";

    public async Task<IReadOnlyList<Game>> GetGamesByCategoryAsync(string categoryId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        IEnumerable<Game> matches = string.Equals(categoryId, FavoritesCategoryId, StringComparison.OrdinalIgnoreCase)
            ? _games!.Where(g => g.IsFavorite)
            : _games!.Where(g => g.CategoryIds.Any(id => string.Equals(id, categoryId, StringComparison.OrdinalIgnoreCase)));

        // Alphabetical by title, always — re-evaluated on every call, so this stays
        // correct as games are added, removed, or re-categorized without any separate
        // "re-sort" step needed anywhere else.
        return matches.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<Game?> GetGameByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _games!.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddOrUpdateAsync(Game game, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var idx = _games!.FindIndex(g => string.Equals(g.Id, game.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _games[idx] = game;
            else _games.Add(game);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            _games!.RemoveAll(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(_games, JsonOptions);
            await File.WriteAllTextAsync(_gamesJsonPath, json, ct);
            _logger.LogInformation("Game catalog saved ({Count} games).", _games!.Count);
        }
        finally { _lock.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_games is not null) return;
        await _lock.WaitAsync(ct);
        try
        {
            if (_games is not null) return;
            if (!File.Exists(_gamesJsonPath))
            {
                _logger.LogWarning("games.json not found at {Path}. Starting with empty catalog.", _gamesJsonPath);
                _games = [];
                return;
            }

            var rawJson = await File.ReadAllTextAsync(_gamesJsonPath, ct);
            var migratedJson = MigrateLegacyCategoryId(rawJson);

            _games = JsonSerializer.Deserialize<List<Game>>(migratedJson, JsonOptions) ?? [];
            _logger.LogInformation("Game catalog loaded: {Count} games.", _games.Count);
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Older catalogs stored a single "categoryId" string per game; games can now
    /// belong to multiple categories via a "categoryIds" array. This converts the old
    /// shape into the new one in memory so existing games.json files keep working
    /// without a manual edit — the migrated shape is written back to disk the next
    /// time the catalog is saved.
    /// </summary>
    private string MigrateLegacyCategoryId(string json)
    {
        var root = JsonNode.Parse(json)?.AsArray();
        if (root is null) return json;

        bool anyMigrated = false;
        foreach (var node in root)
        {
            if (node is not JsonObject obj) continue;
            if (obj.ContainsKey("categoryIds")) continue; // already migrated

            var legacyKey = obj.Select(kvp => kvp.Key)
                .FirstOrDefault(k => string.Equals(k, "categoryId", StringComparison.OrdinalIgnoreCase));
            if (legacyKey is null) continue;

            var legacyValue = obj[legacyKey]?.GetValue<string>();
            obj.Remove(legacyKey);

            var arr = new JsonArray();
            if (!string.IsNullOrWhiteSpace(legacyValue))
                arr.Add(JsonValue.Create(legacyValue));
            obj["categoryIds"] = arr;
            anyMigrated = true;
        }

        if (anyMigrated)
            _logger.LogInformation("Migrated games.json from single categoryId to categoryIds list.");

        return root.ToJsonString();
    }
}
