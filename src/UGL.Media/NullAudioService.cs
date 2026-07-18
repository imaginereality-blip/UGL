using UGL.Core.Interfaces;

namespace UGL.Media;

/// <summary>
/// No-op IAudioService. Used in test contexts and as a safe fallback
/// if LibVLC native libraries are missing at runtime.
/// </summary>
public sealed class NullAudioService : IAudioService
{
    public float MusicVolume  { get; set; } = 0.5f;
    public bool  IsMusicEnabled { get; set; } = true;
    public float SoundVolume  { get; set; } = 1.0f;
    public bool  IsSoundEnabled { get; set; } = true;

    public Task StartAsync(CancellationToken ct = default)                    => Task.CompletedTask;
    public Task SwitchPlaylistAsync(string categoryId, CancellationToken ct = default) => Task.CompletedTask;
    public Task CyclePlaylistAsync(int direction, CancellationToken ct = default) => Task.CompletedTask;
#pragma warning disable CS0067 // intentionally never raised — this is a no-op implementation
    public event Action<string>? PlaylistChanged;
#pragma warning restore CS0067
    public void Pause()         { }
    public void Resume()        { }
    public void Stop()          { }
    public void PlayNavigate()  { }
    public void PlayConfirm()   { }
    public void PlayBack()      { }
    public void PlayError()     { }
    public void Dispose()       { }
}
