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
///   POST /upload/image  multipart body field "image" -> {"name":..,"subfolder":..,
///                        "type":"input"} — makes an image available to LoadImage nodes
///                        by filename.
/// </summary>
public sealed class ComfyUiClient : IComfyUiClient
{
    private const string PromptToken = "{{PROMPT}}";
    private const string DenoiseToken = "{{DENOISE}}";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    // 3 minutes was plenty for the small SD1.5 collage checkpoint, but a ~12B
    // parameter model like Flux.1 Fill Dev can easily take longer than that just to
    // load ~23GB of weights from disk on a cold start, before sampling even begins —
    // confirmed by an actual "Timed out waiting for ComfyUI render" in testing at
    // exactly the old 3-minute mark. 15 minutes gives real headroom for a slow cold
    // load without leaving a genuinely stuck/unreachable server hanging forever.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;
    private readonly IScraperSettingsRepository _settingsRepo;
    private readonly ILogger<ComfyUiClient> _logger;

    public ComfyUiClient(HttpClient http, IScraperSettingsRepository settingsRepo, ILogger<ComfyUiClient> logger)
    {
        _http = http;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    public async Task<byte[]?> GenerateImageAsync(string prompt, IReadOnlyList<byte[]> referenceImages, double denoise = 0.35, string? workflowPathOverride = null, IReadOnlyDictionary<string, double>? extraNumericTokens = null, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ComfyUiEndpoint))
        {
            _logger.LogWarning("ComfyUI endpoint not configured.");
            return null;
        }
        var workflowPath = workflowPathOverride ?? settings.ComfyUiWorkflowPath;
        if (string.IsNullOrWhiteSpace(workflowPath) || !File.Exists(workflowPath))
        {
            _logger.LogWarning("ComfyUI workflow file not found: {Path}", workflowPath);
            return null;
        }

        var baseUrl = settings.ComfyUiEndpoint.TrimEnd('/');

        JsonNode? workflow;
        try
        {
            var text = await File.ReadAllTextAsync(workflowPath, ct);
            workflow = JsonNode.Parse(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse ComfyUI workflow JSON at {Path}.", workflowPath);
            return null;
        }
        if (workflow is null) return null;

        if (!SubstituteToken(workflow, PromptToken, prompt))
        {
            _logger.LogWarning("ComfyUI workflow has no {{PROMPT}} token in any node's inputs — nothing to fill in.");
            return null;
        }

        // Best-effort: only workflows that opt in have a "{{DENOISE}}" token in place
        // of a hardcoded KSampler denoise value; older workflows without it are
        // unaffected.
        SubstituteNumericToken(workflow, DenoiseToken, denoise);

        if (extraNumericTokens is not null)
        {
            foreach (var (token, value) in extraNumericTokens)
            {
                SubstituteNumericToken(workflow, token, value);
            }
        }

        if (referenceImages.Count > 0)
        {
            string? lastUploadedName = null;
            for (int i = 1; i <= referenceImages.Count; i++)
            {
                var token = $"{{{{IMAGE_{i}}}}}";
                if (!ContainsToken(workflow, token)) continue;

                string uploadedName;
                try
                {
                    uploadedName = await UploadImageAsync(baseUrl, referenceImages[i - 1], ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upload reference image {Index} to ComfyUI.", i);
                    continue;
                }
                lastUploadedName = uploadedName;
                SubstituteToken(workflow, token, uploadedName);
            }

            // Fewer reference images than {{IMAGE_n}} tokens in the workflow: reuse the
            // last successfully uploaded image for the remaining tokens instead of
            // leaving the graph with an unresolved LoadImage filename.
            if (lastUploadedName is not null)
            {
                for (int i = referenceImages.Count + 1; ; i++)
                {
                    var token = $"{{{{IMAGE_{i}}}}}";
                    if (!ContainsToken(workflow, token)) break;
                    SubstituteToken(workflow, token, lastUploadedName);
                }
            }
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

    /// <summary>Uploads a reference image to ComfyUI's /upload/image route and returns
    /// the filename it was stored under (for use as a LoadImage node's "image"
    /// input).</summary>
    private async Task<string> UploadImageAsync(string baseUrl, byte[] imageBytes, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        using var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(imageContent, "image", $"{Guid.NewGuid():N}.png");

        using var response = await _http.PostAsync($"{baseUrl}/upload/image", content, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var name = doc.RootElement.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        if (name is null) throw new InvalidOperationException("ComfyUI /upload/image response had no 'name'.");
        return name;
    }

    /// <summary>Recursively checks whether <paramref name="token"/> appears as a
    /// substring of any string value anywhere in the workflow graph (a token commonly
    /// sits alongside other literal text in the same field, e.g. a CLIPTextEncode
    /// "text" input authored as "{{PROMPT}}, poster composition, ..." — this must NOT
    /// require the whole field to equal the token).</summary>
    private static bool ContainsToken(JsonNode node, string token)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                {
                    if (kv.Value is JsonValue val && val.TryGetValue<string>(out var s) && s.Contains(token)) return true;
                    if (kv.Value is JsonObject or JsonArray && ContainsToken(kv.Value!, token)) return true;
                }
                return false;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is JsonValue val && val.TryGetValue<string>(out var s) && s.Contains(token)) return true;
                    if (item is JsonObject or JsonArray && ContainsToken(item!, token)) return true;
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary>Like <see cref="SubstituteToken(JsonNode,string,string)"/> but replaces
    /// matching string tokens with a JSON number instead of a string (e.g. a KSampler
    /// "denoise" input authored as the placeholder string "{{DENOISE}}").</summary>
    private static bool SubstituteNumericToken(JsonNode node, string token, double value)
    {
        bool found = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var val = obj[key];
                    if (val is JsonValue jv && jv.TryGetValue<string>(out var s) && s == token)
                    {
                        obj[key] = JsonValue.Create(value);
                        found = true;
                    }
                    else if (val is JsonObject or JsonArray)
                    {
                        found |= SubstituteNumericToken(val!, token, value);
                    }
                }
                break;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    if (item is JsonValue jv && jv.TryGetValue<string>(out var s) && s == token)
                    {
                        arr[i] = JsonValue.Create(value);
                        found = true;
                    }
                    else if (item is JsonObject or JsonArray)
                    {
                        found |= SubstituteNumericToken(item!, token, value);
                    }
                }
                break;
        }
        return found;
    }

    /// <summary>Recursively walks the workflow graph replacing every occurrence of
    /// <paramref name="token"/> within a string value with <paramref name="value"/> —
    /// a substring replace, not a whole-field match, so a field like
    /// "{{PROMPT}}, poster composition, ..." keeps its surrounding literal text.
    /// Returns true if at least one occurrence was found and replaced.</summary>
    private static bool SubstituteToken(JsonNode node, string token, string value)
    {
        bool found = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var val = obj[key];
                    if (val is JsonValue jv && jv.TryGetValue<string>(out var s) && s.Contains(token))
                    {
                        obj[key] = s.Replace(token, value);
                        found = true;
                    }
                    else if (val is JsonObject or JsonArray)
                    {
                        found |= SubstituteToken(val!, token, value);
                    }
                }
                break;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    if (item is JsonValue jv && jv.TryGetValue<string>(out var s) && s.Contains(token))
                    {
                        arr[i] = s.Replace(token, value);
                        found = true;
                    }
                    else if (item is JsonObject or JsonArray)
                    {
                        found |= SubstituteToken(item!, token, value);
                    }
                }
                break;
        }
        return found;
    }
}
