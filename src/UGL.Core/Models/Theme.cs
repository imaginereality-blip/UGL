namespace UGL.Core.Models;

/// <summary>
/// Represents a fully-described UI theme loaded from themes.json.
/// The Theme Engine consumes this to build Avalonia resource dictionaries
/// at runtime without recompilation.
/// </summary>
public sealed class Theme
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    // --- Colors (hex strings, e.g. "#FF1A1A2E") ---
    public string AccentColor { get; init; } = "#FF0078D4";
    public string BackgroundColor { get; init; } = "#FF1A1A2E";
    public string SurfaceColor { get; init; } = "#FF16213E";
    public string TextPrimaryColor { get; init; } = "#FFFFFFFF";
    public string TextSecondaryColor { get; init; } = "#FFB0B0C0";
    public string SelectionColor { get; init; } = "#FF0F3460";

    // --- Typography ---
    public string FontFamily { get; init; } = "Segoe UI";
    public double TitleFontSize { get; init; } = 28.0;
    public double BodyFontSize { get; init; } = 16.0;

    // --- Layout ---
    public double CardWidth { get; init; } = 260.0;
    public double CardHeight { get; init; } = 390.0;
    public double CardSpacing { get; init; } = 24.0;
    public double CardCornerRadius { get; init; } = 12.0;

    // --- Animation ---
    public double SelectionScaleFactor { get; init; } = 1.12;
    public int AnimationDurationMs { get; init; } = 150;
}
