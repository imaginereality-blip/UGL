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
///   - After an initial delay (400 ms by default), repeat fires every 120 ms
///     while held. This matches console dashboard navigation feel.
///
/// Button mapping / dead zone / repeat timing are configurable via
/// ApplyGamepadProfile (Settings → Controllers — see ControllerProfile). This
/// deliberately applies ONE active profile to every XInput slot rather than a
/// specific profile per specific physical device: XInput's 0-3 slot index has
/// no reliable, simple correlation back to a RawInput HardwarePath (a
/// well-known Windows API gap — XInput and RawInput are separate APIs with no
/// shared device identity), so per-exact-device gamepad mapping isn't
/// achievable without a much more involved cross-API correlation. In practice
/// this covers the common case (one primary gamepad configured for menu nav)
/// cleanly; it's a documented v1 limitation, not an oversight.
/// </summary>
public sealed class XInputPollingService : IInputService, IDisposable
{
    private readonly ILogger<XInputPollingService> _logger;
    private readonly UGL.Core.Interfaces.IControllerProfileRepository _profileRepository;

    public event EventHandler<ControllerInputEvent>? InputReceived;

    /// <summary>Fires for ANY newly-pressed physical button (raw mask, not translated
    /// to a ControllerAction), regardless of the quit-combo/action-dispatch logic
    /// below — used solely by Settings → Controllers' "press a button to remap"
    /// capture flow, which needs the raw bit rather than whatever action it happens
    /// to already trigger. Marshalled onto the UI thread like InputReceived.</summary>
    public event EventHandler<ushort>? RawButtonPressed;

    /// <summary>Fires whenever the raw button mask for any controller changes at all
    /// (press OR release), carrying the new full mask — unlike RawButtonPressed
    /// (edge-triggered, newly-pressed bits only), this is what a live "which buttons
    /// are held right now" display (Settings → Controllers' diagram) needs. Marshalled
    /// onto the UI thread like the other input events.</summary>
    public event EventHandler<ushort>? RawButtonStateChanged;

    /// <summary>
    /// Fires once per newly-active physical input — button press OR axis crossing
    /// past its dead zone/trigger threshold — carrying the canonical binding id from
    /// ControllerControlDefinitions' pools (e.g. "A", "LB", "RTAxis"). This is the
    /// single capture source for Settings → Controllers' generic physical-control
    /// remap table (ControllerProfile.ControlBindings): unlike RawButtonPressed
    /// (buttons only), a Wheel's pedal/axis-type controls need axis capture too, and
    /// unifying both into one event keeps the capture UI from needing two separate
    /// listeners. Fires regardless of SuppressActionDispatch, same reasoning as
    /// RawButtonPressed above. Marshalled onto the UI thread.
    /// </summary>
    public event EventHandler<CapturedInput>? RawInputCaptured;

    /// <summary>
    /// When true, ProcessControllerState still fires RawButtonPressed/
    /// RawButtonStateChanged (so capture and the live diagram highlight keep
    /// working) but skips every normal action dispatch below — CheckButton/
    /// UpdateHeldState/the quit combo/trigger-scroll, all of it. Set by Settings →
    /// Controllers' "press a button to remap" capture flow while it's actually
    /// waiting for a press.
    ///
    /// Without this, capturing a button that already has a live meaning was a real,
    /// confirmed bug: pressing B to assign a row fired the raw capture event AND
    /// simultaneously fired the live "Back" action (B's hardcoded default), which
    /// backed out of/cancelled whatever Settings sub-mode was open at the same time
    /// the capture was trying to complete — every press fought its own side effect,
    /// which read as "stuck in an endless loop, can't assign any button."
    /// </summary>
    public bool SuppressActionDispatch { get; set; }

    /// <summary>Physical-control remap table (Settings → Controllers, generic
    /// device-surface-first table — see ControllerProfile.ControlBindings), applied
    /// as a translation layer in ApplyControlTranslation BEFORE the fixed
    /// action-dispatch below ever sees a button mask. Independent of
    /// _buttonMapOverride (which remaps ControllerAction, i.e. what a button MEANS to
    /// UGL's menu, not what physical position it occupies).</summary>
    private Dictionary<string, string>? _controlBindings;

    /// <summary>Applies a Gamepad profile's ControlBindings. Pass null (or a non-
    /// Gamepad/empty profile) to clear — an identity mapping, same as an unconfigured
    /// device.</summary>
    public void ApplyControlBindings(ControllerProfile? profile)
    {
        _controlBindings = profile is { DeviceType: RawInputDeviceType.Gamepad, ControlBindings.Count: > 0 }
            ? profile.ControlBindings
            : null;
    }

    /// <summary>The hardcoded action->button defaults CheckButton falls back to when
    /// a profile has no override for that action — single source of truth for both
    /// the actual fallback behavior below and Settings → Controllers' display of
    /// "what does this map to right now."</summary>
    public static readonly IReadOnlyDictionary<ControllerAction, ushort> DefaultButtonMap = new Dictionary<ControllerAction, ushort>
    {
        [ControllerAction.PlaylistNext]     = XInput.DPadUp,
        [ControllerAction.PlaylistPrevious] = XInput.DPadDown,
        [ControllerAction.TrackNext]        = XInput.DPadRight,
        [ControllerAction.TrackPrevious]    = XInput.DPadLeft,
        [ControllerAction.Select]           = XInput.ButtonA,
        [ControllerAction.Back]             = XInput.ButtonB,
        [ControllerAction.Secondary]        = XInput.ButtonX,
        [ControllerAction.Info]             = XInput.ButtonY,
        [ControllerAction.CategoryLeft]     = XInput.LeftShoulder,
        [ControllerAction.CategoryRight]    = XInput.RightShoulder,
        [ControllerAction.Start]            = XInput.Start,
        [ControllerAction.FilterOverlay]    = XInput.Back,
    };

    public bool IsControllerConnected { get; private set; }

    // Polling thread state
    private Thread? _pollThread;
    private volatile bool _running;

    // Per-button repeat tracking — keyed by (controller, action), NOT action alone.
    // A single shared Dictionary<ControllerAction,...>/HashSet<ControllerAction> here
    // was a real, confirmed bug: with two controllers connected, controller B
    // reporting "not active" for the same action controller A is actively holding
    // would clear A's held-state every tick (the "not active, but still marked
    // held" branch fires for B regardless of which controller actually set it) —
    // which then looked like a fresh press on the very next tick and re-fired
    // immediately, bypassing the repeat-delay/interval gate entirely. That turned a
    // properly-paced ~8 Hz repeat into a full 60 Hz fire-every-tick loop as soon as
    // a second controller was merely connected, even if untouched — reported as
    // "scrolling moves super fast once I turn on a second controller."
    private readonly Dictionary<(uint Controller, ControllerAction Action), DateTime> _buttonFirstPress = new();
    private readonly Dictionary<(uint Controller, ControllerAction Action), DateTime> _buttonLastRepeat = new();
    private readonly HashSet<(uint Controller, ControllerAction Action)>              _heldButtons       = new();

    // Previous raw button state per controller (for edge detection)
    private readonly ushort[] _prevButtons = new ushort[XInput.MaxControllers];
    private readonly byte[]   _prevLT      = new byte[XInput.MaxControllers];
    private readonly byte[]   _prevRT      = new byte[XInput.MaxControllers];

    // Previous "past threshold" state per axis-type binding, per controller — for
    // RawInputCaptured's edge-triggered axis capture (mirrors _prevButtons/newlyPressed
    // for buttons, but axes have no wButtons bit to compare).
    private readonly bool[] _prevLtAxisActive      = new bool[XInput.MaxControllers];
    private readonly bool[] _prevRtAxisActive      = new bool[XInput.MaxControllers];
    private readonly bool[] _prevLeftStickXActive  = new bool[XInput.MaxControllers];
    private readonly bool[] _prevLeftStickYActive  = new bool[XInput.MaxControllers];
    private readonly bool[] _prevRightStickYActive = new bool[XInput.MaxControllers];

    /// <summary>Canonical binding-id for every standard XInput button bit — the
    /// reverse of MaskForBindingId below, used to turn a newly-pressed raw bit into
    /// the string id RawInputCaptured reports.</summary>
    private static readonly (ushort Mask, string BindingId)[] ButtonBindingBits =
    [
        (XInput.DPadUp, "DPadUp"), (XInput.DPadDown, "DPadDown"), (XInput.DPadLeft, "DPadLeft"), (XInput.DPadRight, "DPadRight"),
        (XInput.Start, "Start"), (XInput.Back, "Back"),
        (XInput.LeftThumb, "L3"), (XInput.RightThumb, "R3"),
        (XInput.LeftShoulder, "LB"), (XInput.RightShoulder, "RB"),
        (XInput.ButtonA, "A"), (XInput.ButtonB, "B"), (XInput.ButtonX, "X"), (XInput.ButtonY, "Y"),
    ];

    // Repeat timing — instance fields (not the original static readonly
    // constants) so ApplyGamepadProfile can override them; default values
    // match what was previously hardcoded.
    private TimeSpan InitialRepeatDelay = TimeSpan.FromMilliseconds(400);
    private TimeSpan RepeatInterval     = TimeSpan.FromMilliseconds(120);

    // Dead zone — same reasoning: instance field, default matches the
    // previous XInput.ThumbDeadZone constant.
    private int _thumbDeadZone = XInput.ThumbDeadZone;

    // Button-mapping override: action -> physical button mask. Only entries a
    // user has actually remapped are present; CheckButton falls back to the
    // hardcoded default mask (passed in at each call site) for anything absent,
    // so a partially-configured or never-configured profile is always safe.
    private Dictionary<ControllerAction, ushort>? _buttonMapOverride;

    private ushort MaskFor(ControllerAction action, ushort defaultMask) =>
        _buttonMapOverride is not null && _buttonMapOverride.TryGetValue(action, out var custom)
            ? custom
            : defaultMask;

    /// <summary>The XInput button bit a Gamepad ControlDefinition's id corresponds to
    /// in ProcessControllerState's existing fixed dispatch below — 0 for L3/R3/the
    /// trigger axes, which the menu-action dispatch never reads as a wButtons bit.</summary>
    private static ushort CanonicalButtonMaskFor(string controlId) => controlId switch
    {
        "A" => XInput.ButtonA, "B" => XInput.ButtonB, "X" => XInput.ButtonX, "Y" => XInput.ButtonY,
        "LeftBumper" => XInput.LeftShoulder, "RightBumper" => XInput.RightShoulder,
        "View" => XInput.Back, "Menu" => XInput.Start,
        "DPadUp" => XInput.DPadUp, "DPadDown" => XInput.DPadDown, "DPadLeft" => XInput.DPadLeft, "DPadRight" => XInput.DPadRight,
        _ => 0,
    };

    /// <summary>The XInput button bit a binding-pool value (ButtonBindingPool) maps
    /// to — 0 for "LT"/"RT" (handled specially, see ApplyControlTranslation: triggers
    /// are analog bytes, not a wButtons bit) and for an empty/unrecognized binding.</summary>
    private static ushort MaskForBindingId(string bindingId) => bindingId switch
    {
        "A" => XInput.ButtonA, "B" => XInput.ButtonB, "X" => XInput.ButtonX, "Y" => XInput.ButtonY,
        "LB" => XInput.LeftShoulder, "RB" => XInput.RightShoulder,
        "Back" => XInput.Back, "Start" => XInput.Start,
        "L3" => XInput.LeftThumb, "R3" => XInput.RightThumb,
        "DPadUp" => XInput.DPadUp, "DPadDown" => XInput.DPadDown, "DPadLeft" => XInput.DPadLeft, "DPadRight" => XInput.DPadRight,
        _ => 0,
    };

    /// <summary>
    /// Translates a raw button mask through _controlBindings before any menu-action
    /// dispatch sees it: for every button-type Gamepad control (A/B/X/Y/bumpers/View/
    /// Menu/D-pad), if the physical input it's currently bound to is held, the
    /// control's OWN canonical bit is set in the result — so e.g. binding "A" to "LB"
    /// makes holding the physical LB button read, to everything below, exactly like
    /// holding the physical A button. A no-op (returns rawButtons unchanged) when no
    /// ControlBindings are configured, so an unconfigured device behaves exactly as
    /// before this existed.
    /// </summary>
    private ushort ApplyControlTranslation(ushort rawButtons, byte lt, byte rt)
    {
        if (_controlBindings is null) return rawButtons;

        ushort translated = 0;
        foreach (var control in ControllerControlDefinitions.Gamepad)
        {
            if (control.Kind != BindingKind.Button) continue;
            var canonicalMask = CanonicalButtonMaskFor(control.ControlId);
            if (canonicalMask == 0) continue; // L3/R3 — not read by menu-action dispatch

            var boundInput = _controlBindings.TryGetValue(control.ControlId, out var b) && !string.IsNullOrEmpty(b)
                ? b
                : control.DefaultBinding;

            bool physicallyHeld = boundInput switch
            {
                "LT" => lt > XInput.TriggerThreshold,
                "RT" => rt > XInput.TriggerThreshold,
                _    => (rawButtons & MaskForBindingId(boundInput)) != 0,
            };

            if (physicallyHeld) translated |= canonicalMask;
        }
        return translated;
    }

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

    public XInputPollingService(ILogger<XInputPollingService> logger, UGL.Core.Interfaces.IControllerProfileRepository profileRepository)
    {
        _logger = logger;
        _profileRepository = profileRepository;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        // Fire-and-forget — a fast local JSON read, but Start() itself stays
        // synchronous (matches IInputService's existing signature). Worst case,
        // the first handful of poll ticks use hardcoded defaults until this
        // completes, which is imperceptible.
        _ = LoadStoredGamepadProfileAsync();

        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name         = "UGL.XInputPoller",
            Priority     = ThreadPriority.AboveNormal,
        };
        _pollThread.Start();
        _logger.LogInformation("XInput polling started.");
    }

    private async Task LoadStoredGamepadProfileAsync()
    {
        try
        {
            var profiles = await _profileRepository.GetAllAsync();
            // IsActive, not just "the first Gamepad entry found" — a device can now
            // have several named profiles (Save As), only one of which is active.
            var gamepad = profiles.FirstOrDefault(p => p.DeviceType == RawInputDeviceType.Gamepad && p.IsActive)
                ?? profiles.FirstOrDefault(p => p.DeviceType == RawInputDeviceType.Gamepad);
            if (gamepad is not null)
            {
                ApplyGamepadProfile(gamepad);
                ApplyControlBindings(gamepad);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load stored gamepad controller profile — using hardcoded defaults.");
        }
    }

    /// <summary>Called by ControllersConfigViewModel.SaveAsync (same DI singleton
    /// instance) right after persisting, so a saved profile takes effect
    /// immediately without waiting for an app restart. Pass null to revert to
    /// hardcoded defaults.</summary>
    public void ApplyGamepadProfile(ControllerProfile? profile)
    {
        _buttonMapOverride = profile is { ButtonMap.Count: > 0 } ? profile.ButtonMap : null;
        _thumbDeadZone = profile?.ThumbDeadZone ?? XInput.ThumbDeadZone;
        InitialRepeatDelay = TimeSpan.FromMilliseconds(profile?.MenuRepeatDelayMs ?? 400);
        RepeatInterval = TimeSpan.FromMilliseconds(profile?.MenuRepeatIntervalMs ?? 120);
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

        // Captured before any translation reassigns `buttons` below — see its use at
        // the end of this method.
        ushort rawButtonsForStorage = buttons;

        ushort newlyPressed = (ushort)(buttons & ~prevBtns);
        if (newlyPressed != 0 && RawButtonPressed is { } handler)
            Dispatcher.UIThread.Post(() => handler(this, newlyPressed));

        if (buttons != prevBtns && RawButtonStateChanged is { } stateHandler)
            Dispatcher.UIThread.Post(() => stateHandler(this, buttons));

        // Unified capture for the generic physical-control remap table — buttons via
        // the same newlyPressed edge already computed above, axes via their own
        // threshold-crossing edge below. Fires regardless of SuppressActionDispatch,
        // same reasoning as RawButtonPressed/RawButtonStateChanged above (capture mode
        // sets SuppressActionDispatch specifically so it can listen for this without
        // a live action firing at the same time).
        if (newlyPressed != 0 && RawInputCaptured is { } capturedHandler)
        {
            foreach (var (mask, bindingId) in ButtonBindingBits)
            {
                if ((newlyPressed & mask) == 0) continue;
                var id = bindingId;
                Dispatcher.UIThread.Post(() => capturedHandler(this, new CapturedInput(id, IsAxis: false)));
            }
        }

        bool ltAxisActive = lt > XInput.TriggerThreshold;
        bool rtAxisActive = rt > XInput.TriggerThreshold;
        bool leftXActive  = Math.Abs((int)pad.sThumbLX) > _thumbDeadZone;
        bool leftYActive  = Math.Abs((int)pad.sThumbLY) > _thumbDeadZone;
        bool rightYActive = Math.Abs((int)pad.sThumbRY) > _thumbDeadZone;

        RaiseAxisCaptureIfNewlyActive(controllerIndex, "LTAxis",      ltAxisActive, _prevLtAxisActive);
        RaiseAxisCaptureIfNewlyActive(controllerIndex, "RTAxis",      rtAxisActive, _prevRtAxisActive);
        RaiseAxisCaptureIfNewlyActive(controllerIndex, "LeftStickX",  leftXActive,  _prevLeftStickXActive);
        RaiseAxisCaptureIfNewlyActive(controllerIndex, "LeftStickY",  leftYActive,  _prevLeftStickYActive);
        RaiseAxisCaptureIfNewlyActive(controllerIndex, "RightStickY", rightYActive, _prevRightStickYActive);

        _prevLtAxisActive[controllerIndex]      = ltAxisActive;
        _prevRtAxisActive[controllerIndex]      = rtAxisActive;
        _prevLeftStickXActive[controllerIndex]  = leftXActive;
        _prevLeftStickYActive[controllerIndex]  = leftYActive;
        _prevRightStickYActive[controllerIndex] = rightYActive;

        if (SuppressActionDispatch)
        {
            _prevButtons[controllerIndex] = buttons;
            _prevLT[controllerIndex]      = lt;
            _prevRT[controllerIndex]      = rt;
            return;
        }

        // Translation layer: from here down, every check reads the physical-control
        // remap table's output, not the raw pad state directly — see
        // ApplyControlTranslation's own doc comment. A no-op when no ControlBindings
        // are configured (buttons/prevBtns pass through unchanged).
        buttons  = ApplyControlTranslation(buttons, lt, rt);
        prevBtns = ApplyControlTranslation(prevBtns, _prevLT[controllerIndex], _prevRT[controllerIndex]);

        // ── D-Pad: repurposed for music control now that Left Stick handles
        // menu navigation (below). Up/Down changes playlist, Left/Right changes
        // track — fires on press only, no repeat-while-held (skipping tracks
        // rapidly on a held button isn't useful the way holding to navigate is).
        CheckButton(controllerIndex, buttons, prevBtns,
            MaskFor(ControllerAction.PlaylistNext, XInput.DPadUp),        ControllerAction.PlaylistNext);
        CheckButton(controllerIndex, buttons, prevBtns,
            MaskFor(ControllerAction.PlaylistPrevious, XInput.DPadDown),  ControllerAction.PlaylistPrevious);
        CheckButton(controllerIndex, buttons, prevBtns,
            MaskFor(ControllerAction.TrackNext, XInput.DPadRight),        ControllerAction.TrackNext);
        CheckButton(controllerIndex, buttons, prevBtns,
            MaskFor(ControllerAction.TrackPrevious, XInput.DPadLeft),     ControllerAction.TrackPrevious);
        CheckButton(controllerIndex, buttons, prevBtns,
            MaskFor(ControllerAction.Select, XInput.ButtonA),             ControllerAction.Select);
        CheckButton(controllerIndex, buttons, prevBtns,
            MaskFor(ControllerAction.Back, XInput.ButtonB),               ControllerAction.Back);
        CheckButton(controllerIndex, buttons, prevBtns,
            MaskFor(ControllerAction.Secondary, XInput.ButtonX),          ControllerAction.Secondary);
        CheckButton(controllerIndex, buttons, prevBtns,
            MaskFor(ControllerAction.Info, XInput.ButtonY),               ControllerAction.Info);

        // ── Quit-to-launcher combo (LB+RB+Start+Back) ─────────────────────
        // Checked before the individual LB/RB/Start/Back actions below so that
        // completing the combo doesn't also fire CategoryLeft/CategoryRight/Start/
        // FilterOverlay on the same frame. While any subset of the combo is held,
        // the individual button checks are skipped entirely (not just suppressed
        // once) so nothing spuriously fires as the combo is built up or released
        // one finger at a time.
        bool comboActive    = (buttons  & XInput.QuitComboMask) == XInput.QuitComboMask;
        bool comboWasActive = (prevBtns & XInput.QuitComboMask) == XInput.QuitComboMask;

        if (comboActive && !comboWasActive)
            FireAction(controllerIndex, ControllerAction.QuitToLauncher);

        if (!comboActive)
        {
            CheckButton(controllerIndex, buttons, prevBtns,
                MaskFor(ControllerAction.CategoryLeft, XInput.LeftShoulder),   ControllerAction.CategoryLeft);
            CheckButton(controllerIndex, buttons, prevBtns,
                MaskFor(ControllerAction.CategoryRight, XInput.RightShoulder), ControllerAction.CategoryRight);
            CheckButton(controllerIndex, buttons, prevBtns,
                MaskFor(ControllerAction.Start, XInput.Start),                ControllerAction.Start);
            CheckButton(controllerIndex, buttons, prevBtns,
                MaskFor(ControllerAction.FilterOverlay, XInput.Back),         ControllerAction.FilterOverlay);
        }

        // ── Left stick: full menu navigation — everything D-Pad used to drive,
        // now that D-Pad is repurposed for music control above. ───────────────
        // Dominant-axis gating: a diagonal push (very easy to do unintentionally
        // while aiming for a pure Left/Right or Up/Down direction) would otherwise
        // fire BOTH axes in the same poll — e.g. cycling scraped images with
        // Left/Right could also nudge Up/Down enough to jump focus to an adjacent
        // field. Only the axis with the larger deflection is considered per poll.
        bool xIsDominant = Math.Abs((int)pad.sThumbLX) >= Math.Abs((int)pad.sThumbLY);
        bool stickLeft  = xIsDominant  && pad.sThumbLX < -_thumbDeadZone;
        bool stickRight = xIsDominant  && pad.sThumbLX >  _thumbDeadZone;
        bool stickUp    = !xIsDominant && pad.sThumbLY >  _thumbDeadZone;
        bool stickDown  = !xIsDominant && pad.sThumbLY < -_thumbDeadZone;

        UpdateHeldState(controllerIndex, stickLeft,  ControllerAction.NavigateLeft);
        UpdateHeldState(controllerIndex, stickRight, ControllerAction.NavigateRight);
        UpdateHeldState(controllerIndex, stickUp,    ControllerAction.NavigateUp);
        UpdateHeldState(controllerIndex, stickDown,  ControllerAction.NavigateDown);

        // ── Right stick: scroll whatever scrollable content is on screen ──
        bool scrollUp   = pad.sThumbRY >  _thumbDeadZone;
        bool scrollDown = pad.sThumbRY < -_thumbDeadZone;

        UpdateHeldState(controllerIndex, scrollUp,   ControllerAction.ScrollUp);
        UpdateHeldState(controllerIndex, scrollDown, ControllerAction.ScrollDown);

        // ── Triggers: fast scroll ─────────────────────────────────────────
        bool ltPressed = lt > XInput.TriggerThreshold;
        bool rtPressed = rt > XInput.TriggerThreshold;
        bool ltWas     = _prevLT[controllerIndex] > XInput.TriggerThreshold;
        bool rtWas     = _prevRT[controllerIndex] > XInput.TriggerThreshold;

        if (ltPressed && !ltWas) FireAction(controllerIndex, ControllerAction.FastScrollLeft);
        if (rtPressed && !rtWas) FireAction(controllerIndex, ControllerAction.FastScrollRight);

        // rawButtonsForStorage, not the (possibly translated) local `buttons` above —
        // next tick's edge detection for RawButtonPressed/RawInputCaptured/
        // RawButtonStateChanged needs the actual physical press, not the translated
        // value, or a translated device would never see its own real button presses
        // captured correctly.
        _prevButtons[controllerIndex] = rawButtonsForStorage;
        _prevLT[controllerIndex]      = lt;
        _prevRT[controllerIndex]      = rt;
    }

    /// <summary>Fires RawInputCaptured for one axis-type binding the instant it
    /// crosses from inactive to active — the axis equivalent of newlyPressed's
    /// button-press edge above.</summary>
    private void RaiseAxisCaptureIfNewlyActive(uint controllerIndex, string bindingId, bool active, bool[] prevState)
    {
        if (active && !prevState[controllerIndex] && RawInputCaptured is { } handler)
            Dispatcher.UIThread.Post(() => handler(this, new CapturedInput(bindingId, IsAxis: true)));
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

        var key = (controllerIndex, action);

        if (isDown && !wasDown)
        {
            // Rising edge — fire immediately and start repeat tracking
            FireAction(controllerIndex, action);
            if (RepeatableActions.Contains(action))
            {
                _buttonFirstPress[key] = DateTime.UtcNow;
                _heldButtons.Add(key);
            }
        }
        else if (!isDown && wasDown)
        {
            // Falling edge — stop repeat
            _heldButtons.Remove(key);
            _buttonFirstPress.Remove(key);
            _buttonLastRepeat.Remove(key);
        }
    }

    /// <summary>
    /// Updates held state for analog inputs (stick) that have no clean edge.
    /// </summary>
    private void UpdateHeldState(uint controllerIndex, bool isActive, ControllerAction action)
    {
        var key = (controllerIndex, action);

        if (isActive && !_heldButtons.Contains(key))
        {
            FireAction(controllerIndex, action);
            _buttonFirstPress[key] = DateTime.UtcNow;
            _heldButtons.Add(key);
        }
        else if (!isActive && _heldButtons.Contains(key))
        {
            _heldButtons.Remove(key);
            _buttonFirstPress.Remove(key);
            _buttonLastRepeat.Remove(key);
        }
    }

    /// <summary>Fires repeat events for any buttons currently held.</summary>
    private void ProcessRepeatActions()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _heldButtons)
        {
            if (!_buttonFirstPress.TryGetValue(key, out var firstPress)) continue;

            bool immediate = ImmediateRepeatActions.Contains(key.Action);
            var initialDelay = immediate ? TimeSpan.Zero : InitialRepeatDelay;
            var elapsed = now - firstPress;
            if (elapsed < initialDelay) continue;

            var interval = immediate ? ScrollRepeatInterval : RepeatInterval;

            if (!_buttonLastRepeat.TryGetValue(key, out var lastRepeat)
                || now - lastRepeat >= interval)
            {
                _buttonLastRepeat[key] = now;
                FireAction(key.Controller, key.Action);
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

/// <summary>One captured physical input, reported by XInputPollingService.RawInputCaptured
/// — see its doc comment. BindingId is one of ControllerControlDefinitions'
/// ButtonBindingPool/AxisBindingPool values, matching IsAxis.</summary>
public sealed record CapturedInput(string BindingId, bool IsAxis);
