namespace UGL.Core.Interfaces;

/// <summary>
/// Submits a user-supplied ComfyUI workflow (exported in API format) to a running
/// ComfyUI server and retrieves the generated image. UGL does not construct or
/// understand the workflow graph itself — see ScraperSettings.ComfyUiWorkflowPath.
/// </summary>
public interface IComfyUiClient
{
    /// <summary>
    /// Loads the configured workflow JSON (or <paramref name="workflowPathOverride"/>,
    /// when given, instead of ScraperSettings.ComfyUiWorkflowPath — used to run a
    /// different, single-image cleanup graph for the "Clean Cover" action), substitutes
    /// the literal token "{{PROMPT}}" wherever it appears with <paramref name="prompt"/>,
    /// substitutes any "{{DENOISE}}" token with <paramref name="denoise"/> (ignored if
    /// the workflow has no such token — older workflows hardcode denoise instead),
    /// substitutes each entry of <paramref name="extraNumericTokens"/> (key is the
    /// literal token text including braces, e.g. "{{WORK_WIDTH}}") the same way —
    /// used for workflow-specific numeric knobs like a working canvas size that has to
    /// be computed at runtime from the actual input image, uploads each entry of
    /// <paramref name="referenceImages"/> to ComfyUI and
    /// substitutes the literal tokens "{{IMAGE_1}}", "{{IMAGE_2}}", ... (one per
    /// uploaded image, reusing the last upload for any remaining tokens) wherever they
    /// appear, submits the workflow to /prompt, polls /history/{id} until the render
    /// completes, and downloads the first output image via /view. Returns the raw image
    /// bytes, or null on any failure (server unreachable, workflow missing/invalid, no
    /// {{PROMPT}} token found, timeout).
    /// </summary>
    Task<byte[]?> GenerateImageAsync(string prompt, IReadOnlyList<byte[]> referenceImages, double denoise = 0.35, string? workflowPathOverride = null, IReadOnlyDictionary<string, double>? extraNumericTokens = null, CancellationToken ct = default);
}
