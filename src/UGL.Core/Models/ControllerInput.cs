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
    CategoryLeft,   // LB
    CategoryRight,  // RB

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
