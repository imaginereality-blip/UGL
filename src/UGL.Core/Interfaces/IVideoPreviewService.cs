namespace UGL.Core.Interfaces;

/// <summary>
/// Provides a single shared LibVLC MediaPlayer for video preview on the selected game card.
/// Only one video plays at a time — the selected card owns the player.
/// The View (GameCard.axaml.cs) attaches its VideoView to the player directly.
/// </summary>
public interface IVideoPreviewService : IDisposable
{
    bool  IsEnabled    { get; }
    bool  AudioEnabled { get; }
    float Volume       { get; set; }

    /// <summary>Starts playing the video at the given absolute path. Stops any current video first.</summary>
    void Play(string absolutePath);

    /// <summary>Stops playback and releases the current media.</summary>
    void Stop();

    /// <summary>
    /// Returns the underlying LibVLCSharp.Shared.MediaPlayer so the View
    /// can attach its VideoView. Returns null if disabled or LibVLC failed to init.
    /// </summary>
    object? GetMediaPlayer();
}
