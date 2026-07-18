namespace UGL.Core.Models;

/// <summary>
/// Holds file paths for all media assets associated with a game.
/// All paths are relative to the configured media root directory.
/// A null or empty string means the asset is absent; the UI
/// falls back to a theme-defined placeholder.
/// </summary>
public sealed class GameMedia
{
    public string CoverPath { get; init; } = string.Empty;
    public string ScreenshotPath { get; init; } = string.Empty;
    public string VideoPath { get; init; } = string.Empty;
    public string LogoPath { get; init; } = string.Empty;
    public string MarqueePath { get; init; } = string.Empty;
    public string WheelArtPath { get; init; } = string.Empty;
    public string BoxArtPath { get; init; } = string.Empty;
    public string BackgroundPath { get; init; } = string.Empty;
}
