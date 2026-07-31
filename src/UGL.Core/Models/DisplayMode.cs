namespace UGL.Core.Models;

/// <summary>
/// An explicit display resolution/refresh-rate to switch to for the duration of a
/// game session — many emulators, and especially lightgun games, only behave
/// correctly at one specific mode (e.g. 4:3 at a particular CRT-style refresh rate).
/// Each field is independently optional: null means "leave that aspect of whatever
/// mode is currently active alone." Applied just before launch and restored to
/// whatever was active beforehand as soon as the session ends.
/// </summary>
public sealed class DisplayMode
{
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? RefreshHz { get; set; }

    /// <summary>True when every field is null — equivalent to "no override configured."</summary>
    public bool IsEmpty => Width is null && Height is null && RefreshHz is null;
}
