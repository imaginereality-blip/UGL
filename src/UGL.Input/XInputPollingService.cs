using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Input;

/// <summary>
/// Polls XInput controllers on a dedicated background thread at ~60 Hz and
/// translates raw button states into semantic <see cref="ControllerAction"/>
/// events. Events are marshalled onto the Avalonia UI thread before firing
/// so that ViewModels never need to call Dispatcher.UIThread.Post themselves.
///
/// Button-repeat behaviour:
///   - First press fires immediately.
///   - After an initial delay of 400 ms, repeat fires every 120 ms while held.
///   This matches console dashboard navigation feel.
/// </summary>
public sealed class XInputPollingService : IInputService, IDisposable
{
    private readonly ILogger<XInputPollingService> _logger;

    public event EventHandler<ControllerInputEvent>? InputReceived;

    public bool IsControllerConnected { get; private set; }

    // Polling thread state
    private Thread? _pollThread;
    private volatile bool _running;

    // Per-button repeat tracking
    private readonly Dictionary<ControllerAction, DateTime> _buttonFirstPress  = new();
    private readonly Dictionary<ControllerAction, DateTime> _buttonLastRepeat  = new();
    private readonly HashSet<ControllerAction>              _heldButtons       = new();

    // Previous raw button state per controller (for edge detection)
    private readonly ushort[] _prevButtons = new ushort[XInput.MaxControllers];
    private readonly byte[]   _prevLT      = new byte[XInput.MaxControllers];
    private readonly byte[]   _prevRT      = new byte[XInput.MaxControllers];

    // Repeat timing constants
    private static readonly TimeSpan InitialRepeatDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RepeatInterval     = TimeSpan.FromMilliseconds(120);

    // Scroll repeats faster and without the initial hesitation delay — a discrete
    // navigation step every 120ms reads as deliberate menu movement, but the same
    // pacing feels sluggish for continuously scrolling a long form with the stick.
    private static readonly TimeSpan ScrollRepeatInterval = TimeSpan.FromMilliseconds(40);
    private static readonly HashSet<ControllerAction> ImmediateRepeatActions = new()
    {
        ControllerAction.ScrollUp,
        ControllerAction.ScrollDown,
    };

    // Navigation actions that support button-repeat when held. Note: NavigateLeft/
    // Right/Up/Down are no longer reached via CheckButton (D-Pad drives different
    // actions now — see ProcessControllerState) — they're driven by the Left Stick
    // via UpdateHeldState instead, which always repeats regardless of this set.
    // Left here as harmless, in case a button is ever remapped back to Navigate*.
    private static readonly HashSet<ControllerAction> RepeatableActions = new()
    {
        ControllerAction.NavigateLeft,
        ControllerAction.NavigateRight,
        ControllerAction.NavigateUp,
        ControllerAction.NavigateDown,
        ControllerAction.FastScrollLeft,
        ControllerAction.FastScrollRight,
    };

    public XInputPollingService(ILogger<XInputPollingService> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name         = "UGL.XInputPoller",
            Priority     = ThreadPriority.AboveNormal,
        };
        _pollThread.Start();
        _logger.LogInformation("XInput polling started.");
    }

    public void Stop()
    {
        _running = false;
        _logger.LogInformation("XInput polling stopped.");
    }

    public void Dispose() => Stop();


    /// <summary>
    /// Allows KeyboardInputService to inject events into the same pipeline.
    /// Must be called on the UI thread.
    /// </summary>
    internal void RaiseInput(ControllerInputEvent evt)
        => InputReceived?.Invoke(this, evt);

    // ── Polling loop (runs on background thread) ──────────────────────────

    private void PollLoop()
    {
        while (_running)
        {
            bool anyConnected = false;

            for (uint i = 0; i < XInput.MaxControllers; i++)
            {
                uint result = XInput.GetState(i, out var state);
                if (result != XInput.Success)
                {
                    // Controller slot is empty — clear its previous state
                    _prevButtons[i] = 0;
                    _prevLT[i]      = 0;
                    _prevRT[i]      = 0;
                    continue;
                }

                anyConnected = true;
                ProcessControllerState(i, state.Gamepad);
            }

            IsControllerConnected = anyConnected;
            ProcessRepeatActions();

            // ~60 Hz poll rate
            Thread.Sleep(16);
        }
    }

    private void ProcessControllerState(uint controllerIndex, XInputGamepad pad)
    {
        ushort buttons  = pad.wButtons;
        ushort prevBtns = _prevButtons[controllerIndex];
        byte   lt       = pad.bLeftTrigger;
        byte   rt       = pad.bRightTrigger;

        // ── D-Pad: repurposed for music control now that Left Stick handles
        // menu navigation (below). Up/Down changes playlist, Left/Right changes
        // track — fires on press only, no repeat-while-held (skipping tracks
        // rapidly on a held button isn't useful the way holding to navigate is).
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.DPadUp,        ControllerAction.PlaylistNext);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.DPadDown,      ControllerAction.PlaylistPrevious);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.DPadRight,     ControllerAction.TrackNext);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.DPadLeft,      ControllerAction.TrackPrevious);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.LeftShoulder,  ControllerAction.CategoryLeft);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.RightShoulder, ControllerAction.CategoryRight);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.ButtonA,       ControllerAction.Select);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.ButtonB,       ControllerAction.Back);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.ButtonX,       ControllerAction.Secondary);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.ButtonY,       ControllerAction.Info);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.Start,         ControllerAction.Start);
        CheckButton(controllerIndex, buttons, prevBtns,
            XInput.Back,          ControllerAction.FilterOverlay);

        // ── Left stick: full menu navigation — everything D-Pad used to drive,
        // now that D-Pad is repurposed for music control above. ───────────────
        bool stickLeft  = pad.sThumbLX < -XInput.ThumbDeadZone;
        bool stickRight = pad.sThumbLX >  XInput.ThumbDeadZone;
        bool stickUp    = pad.sThumbLY >  XInput.ThumbDeadZone;
        bool stickDown  = pad.sThumbLY < -XInput.ThumbDeadZone;

        UpdateHeldState(controllerIndex, stickLeft,  ControllerAction.NavigateLeft);
        UpdateHeldState(controllerIndex, stickRight, ControllerAction.NavigateRight);
        UpdateHeldState(controllerIndex, stickUp,    ControllerAction.NavigateUp);
        UpdateHeldState(controllerIndex, stickDown,  ControllerAction.NavigateDown);

        // ── Right stick: scroll whatever scrollable content is on screen ──
        bool scrollUp   = pad.sThumbRY >  XInput.ThumbDeadZone;
        bool scrollDown = pad.sThumbRY < -XInput.ThumbDeadZone;

        UpdateHeldState(controllerIndex, scrollUp,   ControllerAction.ScrollUp);
        UpdateHeldState(controllerIndex, scrollDown, ControllerAction.ScrollDown);

        // ── Triggers: fast scroll ─────────────────────────────────────────
        bool ltPressed = lt > XInput.TriggerThreshold;
        bool rtPressed = rt > XInput.TriggerThreshold;
        bool ltWas     = _prevLT[controllerIndex] > XInput.TriggerThreshold;
        bool rtWas     = _prevRT[controllerIndex] > XInput.TriggerThreshold;

        if (ltPressed && !ltWas) FireAction(controllerIndex, ControllerAction.FastScrollLeft);
        if (rtPressed && !rtWas) FireAction(controllerIndex, ControllerAction.FastScrollRight);

        _prevButtons[controllerIndex] = buttons;
        _prevLT[controllerIndex]      = lt;
        _prevRT[controllerIndex]      = rt;
    }

    /// <summary>Detects rising edge (newly pressed) and updates held state.</summary>
    private void CheckButton(
        uint controllerIndex,
        ushort current, ushort previous,
        ushort mask,
        ControllerAction action)
    {
        bool isDown  = (current  & mask) != 0;
        bool wasDown = (previous & mask) != 0;

        if (isDown && !wasDown)
        {
            // Rising edge — fire immediately and start repeat tracking
            FireAction(controllerIndex, action);
            if (RepeatableActions.Contains(action))
            {
                _buttonFirstPress[action] = DateTime.UtcNow;
                _heldButtons.Add(action);
            }
        }
        else if (!isDown && wasDown)
        {
            // Falling edge — stop repeat
            _heldButtons.Remove(action);
            _buttonFirstPress.Remove(action);
            _buttonLastRepeat.Remove(action);
        }
    }

    /// <summary>
    /// Updates held state for analog inputs (stick) that have no clean edge.
    /// </summary>
    private void UpdateHeldState(uint controllerIndex, bool isActive, ControllerAction action)
    {
        if (isActive && !_heldButtons.Contains(action))
        {
            FireAction(controllerIndex, action);
            _buttonFirstPress[action] = DateTime.UtcNow;
            _heldButtons.Add(action);
        }
        else if (!isActive && _heldButtons.Contains(action))
        {
            _heldButtons.Remove(action);
            _buttonFirstPress.Remove(action);
            _buttonLastRepeat.Remove(action);
        }
    }

    /// <summary>Fires repeat events for any buttons currently held.</summary>
    private void ProcessRepeatActions()
    {
        var now = DateTime.UtcNow;
        foreach (var action in _heldButtons)
        {
            if (!_buttonFirstPress.TryGetValue(action, out var firstPress)) continue;

            bool immediate = ImmediateRepeatActions.Contains(action);
            var initialDelay = immediate ? TimeSpan.Zero : InitialRepeatDelay;
            var elapsed = now - firstPress;
            if (elapsed < initialDelay) continue;

            var interval = immediate ? ScrollRepeatInterval : RepeatInterval;

            if (!_buttonLastRepeat.TryGetValue(action, out var lastRepeat)
                || now - lastRepeat >= interval)
            {
                _buttonLastRepeat[action] = now;
                FireAction(0, action);
            }
        }
    }

    /// <summary>
    /// Marshals the event onto the Avalonia UI thread.
    /// ViewModels receive all events on the UI thread and need no extra dispatch.
    /// </summary>
    private void FireAction(uint controllerIndex, ControllerAction action)
    {
        var evt = new ControllerInputEvent
        {
            ControllerIndex = (int)controllerIndex,
            Action          = action,
        };

        Dispatcher.UIThread.Post(() => InputReceived?.Invoke(this, evt));
    }
}
