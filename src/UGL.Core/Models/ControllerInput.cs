namespace UGL.Core.Models;

/// <summary>
/// Represents a single discrete controller action mapped from a raw XInput
/// button press. The Input service translates hardware events into these
/// semantic actions; the UI layer responds to actions, never raw buttons.
/// </summary>
public enum ControllerAction
{
    None,

    // Face buttons
    Select,       // A
    Back,         // B
    Secondary,    // X
    Info,         // Y

    // Shoulder
    CategoryLeft,   // LB (used only within Settings for sub-tab switching now —
                    // freed up outside Settings for a future function)
    CategoryRight,  // RB (same)

    // D-Pad — repurposed for music control now that Left Stick handles menu
    // navigation. Up/Down changes the active playlist, Left/Right changes the
    // current track within it.
    PlaylistNext,     // D-Pad Up
    PlaylistPrevious, // D-Pad Down
    TrackNext,        // D-Pad Right
    TrackPrevious,    // D-Pad Left

    // Triggers
    FastScrollLeft,  // LT
    FastScrollRight, // RT

    // D-Pad / Stick navigation
    NavigateLeft,
    NavigateRight,
    NavigateUp,
    NavigateDown,

    // Right stick — scrolling within scrollable content
    ScrollUp,
    ScrollDown,

    // System
    FilterOverlay,  // HOME button
    Start,

    // Quit-to-launcher: LB+RB+Start+Back held simultaneously while a game/emulator
    // is running — kills the running process and returns focus to UGL. Fires as its
    // own action rather than overloading the individual button actions, since those
    // four buttons already have unrelated meanings (CategoryLeft/Right, Start,
    // FilterOverlay) that must not also fire when the combo completes.
    QuitToLauncher,
}

/// <summary>
/// Event payload raised by IInputService when a controller action fires.
/// </summary>
public sealed class ControllerInputEvent
{
    public required int ControllerIndex { get; init; }
    public required ControllerAction Action { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
