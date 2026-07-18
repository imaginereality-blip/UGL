namespace UGL.Core.Models;

/// <summary>
/// Represents a top-level navigation category (e.g. Fighting, Racing, FPS).
/// Categories are data-driven from categories.json, never hard-coded.
///
/// ArtPath and VideoPath store absolute or exe-relative paths to card art
/// and video preview — any Windows-compatible filename is valid.
/// BackgroundPath, AccentColor, and Description support richer per-category
/// theming managed via the Theme/Category config editor.
/// </summary>
public sealed class Category
{
    public required string Id    { get; init; }
    public required string Label { get; init; }

    /// <summary>Sort order within the main menu.</summary>
    public int Order { get; init; }

    /// <summary>Optional icon resource key used by the Theme Engine.</summary>
    public string IconKey { get; set; } = string.Empty;

    /// <summary>
    /// Absolute or exe-relative path to the card art image (JPG, PNG, WebP).
    /// Empty = no art assigned, placeholder letter is shown.
    /// Any Windows-compatible filename is valid — no naming convention required.
    /// </summary>
    public string ArtPath { get; set; } = string.Empty;

    /// <summary>
    /// Absolute or exe-relative path to the card video preview (MP4, MKV, AVI).
    /// Empty = no video, art or placeholder is shown instead.
    /// Any Windows-compatible filename is valid.
    /// </summary>
    public string VideoPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional full-screen background image shown when this category is selected.
    /// Absolute or exe-relative path. Any filename is valid.
    /// </summary>
    public string BackgroundPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional per-category accent colour override (hex, e.g. "#FF4444").
    /// Empty = use the active theme's AccentColor.
    /// </summary>
    public string AccentColor { get; set; } = string.Empty;

    /// <summary>
    /// Short description shown in the hint bar subtitle when this category is focused.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
