namespace UGL.Core.Models;

/// <summary>
/// A single audio file in the shared track library. Added once, then referenced by
/// any number of playlists via Id — so the same song doesn't need to be re-imported
/// per playlist, and moving/renaming a file only needs updating in one place.
/// Loaded from config/tracks.json.
/// </summary>
public sealed class AudioTrack
{
    public required string Id { get; set; }

    /// <summary>Display name — defaults to the filename when a track is added, but
    /// can be renamed independently of the actual file.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute or app-relative path to the audio file (.mp3, .ogg, .wav, .flac).</summary>
    public string Path { get; set; } = string.Empty;
}
