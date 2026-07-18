using Avalonia.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Input;

/// <summary>
/// Translates Avalonia <see cref="KeyEventArgs"/> into the same
/// <see cref="ControllerInputEvent"/> stream as the XInput service.
/// The main window calls <see cref="HandleKeyDown"/> on every key press.
///
/// Keyboard mapping:
///   Arrow Left/Right  → NavigateLeft / NavigateRight
///   Arrow Up/Down     → NavigateUp   / NavigateDown
///   Q / E             → CategoryLeft / CategoryRight
///   Enter / Space     → Select
///   Escape / Backspace→ Back
///   Tab               → FilterOverlay
/// </summary>
public sealed class KeyboardInputService
{
    private readonly ILogger<KeyboardInputService> _logger;
    private readonly IInputService _inputService;

    // Maps Key → ControllerAction
    private static readonly Dictionary<Key, ControllerAction> KeyMap = new()
    {
        [Key.Left]      = ControllerAction.NavigateLeft,
        [Key.Right]     = ControllerAction.NavigateRight,
        [Key.Up]        = ControllerAction.NavigateUp,
        [Key.Down]      = ControllerAction.NavigateDown,
        [Key.Q]         = ControllerAction.CategoryLeft,
        [Key.E]         = ControllerAction.CategoryRight,
        [Key.Enter]     = ControllerAction.Select,
        [Key.Space]     = ControllerAction.Select,
        [Key.Escape]    = ControllerAction.Back,
        [Key.Back]      = ControllerAction.Back,
        [Key.Tab]       = ControllerAction.FilterOverlay,
    };

    public KeyboardInputService(IInputService inputService, ILogger<KeyboardInputService> logger)
    {
        _inputService = inputService;
        _logger       = logger;
    }

    /// <summary>
    /// Call this from the main window's KeyDown handler.
    /// Returns true if the key was mapped and consumed.
    /// </summary>
    public bool HandleKeyDown(Key key)
    {
        if (!KeyMap.TryGetValue(key, out var action))
            return false;

        _logger.LogDebug("Keyboard → {Action}", action);

        // Re-use the same event type and pipeline as the controller
        var evt = new ControllerInputEvent
        {
            ControllerIndex = -1,   // -1 = keyboard (not a real controller slot)
            Action          = action,
        };

        // Fire directly — we're already on the UI thread for keyboard events
        (_inputService as XInputPollingService)?.RaiseInput(evt);

        return true;
    }
}
