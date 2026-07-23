namespace UGL.Core.Models;

/// <summary>
/// A named, user-created playlist. References tracks from the shared library
/// (AudioTrack) by Id rather than duplicating file paths, so the same song can
/// belong to multiple playlists without being re-imported each time.
///
/// Assignment is independent, not exclusive: IsGlobal and CategoryIds can both be
/// set (or neither, while a playlist is still being built out). IsGlobal is
/// meant to be exclusive in practice — only one playlist should be the Global one
/// at a time — enforced by the ViewModel/save logic, not by this model itself.
///
/// Loaded from config/audio.json.
/// </summary>
public sealed class AudioPlaylist
{
    public required string Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>References into the shared track library (AudioTrack.Id). A track Id
    /// that no longer exists in the library is silently skipped during playback,
    /// same as a missing file always was.</summary>
    public List<string> TrackIds { get; set; } = [];

    public bool Shuffle { get; set; } = true;
    public float Volume { get; set; } = 0.5f;

    /// <summary>True if this is THE global/default playlist — plays when no
    /// category-specific playlist is assigned to whatever's currently browsed.</summary>
    public bool IsGlobal { get; set; } = false;

    /// <summary>Categories this playlist plays for automatically when browsed.
    /// A playlist can be assigned to any number of categories, or none.</summary>
    public List<string> CategoryIds { get; set; } = [];
}
