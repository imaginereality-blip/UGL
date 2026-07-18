namespace UGL.Core.Models;

/// <summary>
/// A named playlist of audio file paths.
/// Used for both the global background music playlist and
/// per-category overrides. Loaded from config/audio.json.
/// </summary>
public sealed class AudioPlaylist
{
    /// <summary>
    /// Unique identifier. "global" for the global playlist;
    /// a category Id (e.g. "fighting") for per-category overrides.
    /// </summary>
    public required string Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute or {exe}-relative paths to audio files (.mp3, .ogg, .wav, .flac).</summary>
    public List<string> Tracks { get; set; } = [];

    public bool Shuffle { get; set; } = true;
    public float Volume { get; set; } = 0.5f;
}
