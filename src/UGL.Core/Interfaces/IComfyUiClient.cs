namespace UGL.Core.Interfaces;

/// <summary>
/// Submits a user-supplied ComfyUI workflow (exported in API format) to a running
/// ComfyUI server and retrieves the generated image. UGL does not construct or
/// understand the workflow graph itself — see ScraperSettings.ComfyUiWorkflowPath.
/// </summary>
public interface IComfyUiClient
{
    /// <summary>
    /// Loads the configured workflow JSON, substitutes the literal token "{{PROMPT}}"
    /// wherever it appears with <paramref name="prompt"/>, submits it to /prompt,
    /// polls /history/{id} until the render completes, and downloads the first output
    /// image via /view. Returns the raw image bytes, or null on any failure (server
    /// unreachable, workflow missing/invalid, no {{PROMPT}} token found, timeout).
    /// </summary>
    Task<byte[]?> GenerateImageAsync(string prompt, CancellationToken ct = default);
}
