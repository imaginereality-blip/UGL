using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;

namespace UGL.Scraping;

/// <summary>
/// ComfyUI API contract verified against ComfyUI's own official docs
/// (docs.comfy.org/development/comfyui-server/comms_routes), not assumed from
/// memory:
///   POST /prompt        body {"prompt": &lt;workflow graph&gt;, "client_id": "..."}
///                        -> {"prompt_id": "...", "number": N} (or {"error":...} on
///                        validation failure — the workflow itself is invalid/
///                        incompatible with the user's installed nodes/models, which
///                        UGL has no way to diagnose since it doesn't understand the
///                        graph).
///   GET  /history/{id}   -> {"{id}": {"outputs": {"{nodeId}": {"images": [
///                        {"filename":..,"subfolder":..,"type":"output"}]}}}}}
///                        — empty/missing until the render actually finishes.
///   GET  /view?filename=..&amp;subfolder=..&amp;type=..  -> raw image bytes.
/// </summary>
public sealed class ComfyUiClient : IComfyUiClient
{
    private const string PromptToken = "{{PROMPT}}";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private readonly HttpClient _http;
    private readonly IScraperSettingsRepository _settingsRepo;
    private readonly ILogger<ComfyUiClient> _logger;

    public ComfyUiClient(HttpClient http, IScraperSettingsRepository settingsRepo, ILogger<ComfyUiClient> logger)
    {
        _http = http;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    public async Task<byte[]?> GenerateImageAsync(string prompt, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ComfyUiEndpoint))
        {
            _logger.LogWarning("ComfyUI endpoint not configured.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(settings.ComfyUiWorkflowPath) || !File.Exists(settings.ComfyUiWorkflowPath))
        {
            _logger.LogWarning("ComfyUI workflow file not found: {Path}", settings.ComfyUiWorkflowPath);
            return null;
        }

        var baseUrl = settings.ComfyUiEndpoint.TrimEnd('/');

        JsonNode? workflow;
        try
        {
            var text = await File.ReadAllTextAsync(settings.ComfyUiWorkflowPath, ct);
            workflow = JsonNode.Parse(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse ComfyUI workflow JSON at {Path}.", settings.ComfyUiWorkflowPath);
            return null;
        }
        if (workflow is null) return null;

        if (!SubstitutePromptToken(workflow, prompt))
        {
            _logger.LogWarning("ComfyUI workflow has no {{PROMPT}} token in any node's inputs — nothing to fill in.");
            return null;
        }

        string clientId = Guid.NewGuid().ToString("N");
        string promptId;
        try
        {
            var submitBody = new JsonObject { ["prompt"] = workflow, ["client_id"] = clientId };
            using var response = await _http.PostAsync($"{baseUrl}/prompt",
                new StringContent(submitBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json"), ct);

            var responseText = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ComfyUI /prompt submission failed ({Status}): {Body}", response.StatusCode, responseText);
                return null;
            }

            using var doc = JsonDocument.Parse(responseText);
            if (doc.RootElement.TryGetProperty("error", out var errorEl))
            {
                _logger.LogWarning("ComfyUI rejected the workflow: {Error}", errorEl.ToString());
                return null;
            }
            if (!doc.RootElement.TryGetProperty("prompt_id", out var idEl) || idEl.GetString() is not { } id)
            {
                _logger.LogWarning("ComfyUI /prompt response had no prompt_id: {Body}", responseText);
                return null;
            }
            promptId = id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to submit ComfyUI workflow.");
            return null;
        }

        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, ct);

            (string filename, string subfolder, string type)? output;
            try
            {
                using var response = await _http.GetAsync($"{baseUrl}/history/{promptId}", ct);
                if (!response.IsSuccessStatusCode) continue;

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
                if (!doc.RootElement.TryGetProperty(promptId, out var entryEl)) continue;
                if (!entryEl.TryGetProperty("outputs", out var outputsEl)) continue;

                output = null;
                foreach (var node in outputsEl.EnumerateObject())
                {
                    if (!node.Value.TryGetProperty("images", out var imagesEl) || imagesEl.ValueKind != JsonValueKind.Array) continue;
                    var first = imagesEl.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind != JsonValueKind.Object) continue;

                    var filename = first.TryGetProperty("filename", out var f) ? f.GetString() : null;
                    if (filename is null) continue;
                    var subfolder = first.TryGetProperty("subfolder", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                    var type = first.TryGetProperty("type", out var t) ? t.GetString() ?? "output" : "output";
                    output = (filename, subfolder, type);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error polling ComfyUI /history/{Id}.", promptId);
                continue;
            }

            if (output is null) continue;

            try
            {
                var viewUrl = $"{baseUrl}/view?filename={Uri.EscapeDataString(output.Value.filename)}" +
                              $"&subfolder={Uri.EscapeDataString(output.Value.subfolder)}&type={Uri.EscapeDataString(output.Value.type)}";
                using var imgResponse = await _http.GetAsync(viewUrl, ct);
                if (!imgResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ComfyUI /view failed: {Status}", imgResponse.StatusCode);
                    return null;
                }
                return await imgResponse.Content.ReadAsByteArrayAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download ComfyUI output image.");
                return null;
            }
        }

        _logger.LogWarning("Timed out waiting for ComfyUI render (prompt_id={Id}).", promptId);
        return null;
    }

    /// <summary>Recursively walks the workflow graph replacing the {{PROMPT}} literal
    /// wherever it appears as a string value. Returns true if at least one occurrence
    /// was found and replaced.</summary>
    private static bool SubstitutePromptToken(JsonNode node, string prompt)
    {
        bool found = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var value = obj[key];
                    if (value is JsonValue val && val.TryGetValue<string>(out var s) && s == PromptToken)
                    {
                        obj[key] = prompt;
                        found = true;
                    }
                    else if (value is JsonObject or JsonArray)
                    {
                        found |= SubstitutePromptToken(value!, prompt);
                    }
                }
                break;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    if (item is JsonValue val && val.TryGetValue<string>(out var s) && s == PromptToken)
                    {
                        arr[i] = prompt;
                        found = true;
                    }
                    else if (item is JsonObject or JsonArray)
                    {
                        found |= SubstitutePromptToken(item!, prompt);
                    }
                }
                break;
        }
        return found;
    }
}
