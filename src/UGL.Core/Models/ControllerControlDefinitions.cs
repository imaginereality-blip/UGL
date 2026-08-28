namespace UGL.Core.Models;

/// <summary>
/// One physical input a control can be bound to. Buttons are digital (XInput button
/// bits, including L3/R3 stick-clicks); axes are analog (triggers and thumbstick
/// halves) and need their own threshold-crossing capture rather than a simple bit
/// check — see XInputPollingService's capture logic.
/// </summary>
public enum BindingKind { Button, Axis }

/// <summary>
/// One named control on a device (e.g. "Left Trigger", "Clutch Pedal") and what it's
/// currently bound to. This is deliberately NOT keyed by ControllerAction — unlike
/// ControllerProfile.ButtonMap (which remaps UGL's own menu actions), this is a
/// generic physical-control remap table matching how a real controller names its own
/// surface, consumed as a translation layer in front of the existing menu-action
/// dispatch for Gamepad, and by RetroArch config generation for Lightgun/Wheel.
/// IsCombo marks the one control (Wheel H-Pattern's "Reverse") whose binding is two
/// simultaneously-held buttons rather than one.
/// </summary>
public sealed record ControlDefinition(string ControlId, string Label, BindingKind Kind, string DefaultBinding, bool IsCombo = false);

/// <summary>
/// Fixed control lists and binding pools for each configurable device class — exact
/// per the controller-configuration design handoff (design_handoff_controller_config/
/// README.md). Shared between UGL.Input (XInputPollingService's translation layer and
/// capture) and UGL.App (the Controllers tab UI), so both sides agree on control ids
/// and legal binding values without duplicating the list.
/// </summary>
public static class ControllerControlDefinitions
{
    /// <summary>Digital binding pool — every standard XInput button, including the
    /// two stick-clicks (L3/R3) that no ControllerAction uses today.</summary>
    public static readonly string[] ButtonBindingPool =
    [
        "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "Back", "Start", "L3", "R3",
        "DPadUp", "DPadDown", "DPadLeft", "DPadRight",
    ];

    /// <summary>Analog binding pool — pedals and thumbstick halves, for Wheel controls.</summary>
    public static readonly string[] AxisBindingPool =
    [
        "RTAxis", "LTAxis", "LeftStickY", "RightStickY", "LeftStickX",
    ];

    public static readonly ControlDefinition[] Gamepad =
    [
        new("LeftTrigger",   "Left Trigger",        BindingKind.Axis,   "LTAxis"),
        new("RightTrigger",  "Right Trigger",       BindingKind.Axis,   "RTAxis"),
        new("LeftBumper",    "Left Bumper",         BindingKind.Button, "LB"),
        new("RightBumper",   "Right Bumper",        BindingKind.Button, "RB"),
        new("L3",            "Left Stick Click",    BindingKind.Button, "L3"),
        new("R3",            "Right Stick Click",   BindingKind.Button, "R3"),
        new("DPadUp",        "D-Pad Up",            BindingKind.Button, "DPadUp"),
        new("DPadDown",      "D-Pad Down",          BindingKind.Button, "DPadDown"),
        new("DPadLeft",      "D-Pad Left",          BindingKind.Button, "DPadLeft"),
        new("DPadRight",     "D-Pad Right",         BindingKind.Button, "DPadRight"),
        new("A",              "A",                  BindingKind.Button, "A"),
        new("B",              "B",                  BindingKind.Button, "B"),
        new("X",              "X",                  BindingKind.Button, "X"),
        new("Y",              "Y",                  BindingKind.Button, "Y"),
        new("View",           "View (Back)",        BindingKind.Button, "Back"),
        new("Menu",           "Menu (Start)",        BindingKind.Button, "Start"),
    ];

    /// <summary>
    /// The real X-Gunner control surface — traced from the user's own annotated
    /// reference (design_handoff_controller_config/vector-source/lightgun-fixed.svg),
    /// not the earlier guessed Trigger/Pump/A/B/Back/Start/Guide list. The device
    /// reports as a D-pad plus 10 generic numbered buttons plus 3 mouse-click
    /// buttons — there is no separate analog stick. Two nearby labels in the source
    /// file, "RECOIL ON/OFF" and "RAPID FIRE ON/OFF", are deliberately NOT
    /// represented here at all — confirmed physical toggle switches on the gun
    /// itself with no PC/Windows input effect (they change onboard firmware
    /// behavior, e.g. the haptic recoil motor), so there's nothing for Windows,
    /// RawInput, or this control list to ever see.
    ///
    /// DefaultBinding is the identity (ControlId itself): unlike Gamepad, this
    /// device isn't XInput-class — its inputs arrive via RawInput, not through
    /// XInputPollingService, so there is no separate "physical XInput input this
    /// maps to" the way there is for Gamepad. The device also supports an alternate
    /// joystick-report mode for some Windows FPS games (movement stick + aim axes +
    /// buttons) distinct from its mouse+HID-button mode — see the RawInput capture
    /// work this list is waiting on.
    /// </summary>
    public static readonly ControlDefinition[] Lightgun =
    [
        new("DPadUp",    "D-Pad Up",    BindingKind.Button, "DPadUp"),
        new("DPadDown",  "D-Pad Down",  BindingKind.Button, "DPadDown"),
        new("DPadLeft",  "D-Pad Left",  BindingKind.Button, "DPadLeft"),
        new("DPadRight", "D-Pad Right", BindingKind.Button, "DPadRight"),
        new("Button1",  "Button 1",  BindingKind.Button, "Button1"),
        new("Button2",  "Button 2",  BindingKind.Button, "Button2"),
        new("Button3",  "Button 3",  BindingKind.Button, "Button3"),
        new("Button4",  "Button 4",  BindingKind.Button, "Button4"),
        new("Button5",  "Button 5",  BindingKind.Button, "Button5"),
        new("Button6",  "Button 6",  BindingKind.Button, "Button6"),
        new("Button7",  "Button 7",  BindingKind.Button, "Button7"),
        new("Button8",  "Button 8",  BindingKind.Button, "Button8"),
        new("Button9",  "Button 9",  BindingKind.Button, "Button9"),
        new("Button10", "Button 10", BindingKind.Button, "Button10"),
        new("MouseLeft",   "Mouse Left Click",   BindingKind.Button, "MouseLeft"),
        new("MouseRight",  "Mouse Right Click",  BindingKind.Button, "MouseRight"),
        new("MouseMiddle", "Mouse Middle Click", BindingKind.Button, "MouseMiddle"),
    ];

    /// <summary>The 15 controls every Wheel profile has regardless of shifter mode —
    /// see WheelShifterSequential/HPattern/PaddlesOnly for the mode-dependent set
    /// appended after these.</summary>
    public static readonly ControlDefinition[] Wheel =
    [
        new("Y",            "Y",             BindingKind.Button, "Y"),
        new("X",            "X",             BindingKind.Button, "X"),
        new("B",            "B",             BindingKind.Button, "B"),
        new("A",            "A",             BindingKind.Button, "A"),
        new("Start",        "Start",         BindingKind.Button, "Start"),
        new("Back",         "Back",          BindingKind.Button, "Back"),
        new("DPadUp",       "D-Pad Up",      BindingKind.Button, "DPadUp"),
        new("DPadDown",     "D-Pad Down",    BindingKind.Button, "DPadDown"),
        new("DPadLeft",     "D-Pad Left",    BindingKind.Button, "DPadLeft"),
        new("DPadRight",    "D-Pad Right",   BindingKind.Button, "DPadRight"),
        new("LeftPaddle",   "Left Paddle",   BindingKind.Button, "LB"),
        new("RightPaddle",  "Right Paddle",  BindingKind.Button, "RB"),
        new("GasPedal",     "Gas Pedal",     BindingKind.Axis,   "RTAxis"),
        new("BrakePedal",   "Brake Pedal",   BindingKind.Axis,   "LTAxis"),
        new("ClutchPedal",  "Clutch Pedal",  BindingKind.Axis,   "LeftStickY"),
    ];

    public enum WheelShifterMode { Sequential, HPattern, PaddlesOnly }

    public static readonly ControlDefinition[] WheelShifterSequential =
    [
        new("ShiftUp",   "Shift Up",   BindingKind.Button, "RB"),
        new("ShiftDown", "Shift Down", BindingKind.Button, "LB"),
    ];

    public static readonly ControlDefinition[] WheelShifterHPattern =
    [
        new("Gear1", "Gear 1", BindingKind.Button, "X"),
        new("Gear2", "Gear 2", BindingKind.Button, "A"),
        new("Gear3", "Gear 3", BindingKind.Button, "B"),
        new("Gear4", "Gear 4", BindingKind.Button, "Y"),
        new("Gear5", "Gear 5", BindingKind.Button, "LB"),
        new("Gear6", "Gear 6", BindingKind.Button, "RB"),
        // Combo: two simultaneously-held buttons, not a single input — see
        // ControlDefinition.IsCombo and XInputPollingService's chord-capture handling.
        new("Reverse", "Reverse", BindingKind.Button, "L3+R3", IsCombo: true),
    ];

    /// <summary>Empty — Paddles Only mode has no separate shifter mapping (the base
    /// Wheel list's LeftPaddle/RightPaddle already handle shifting). The UI shows an
    /// explanatory note instead of a row list for this mode.</summary>
    public static readonly ControlDefinition[] WheelShifterPaddlesOnly = [];

    public static ControlDefinition[] GetWheelShifterControls(WheelShifterMode mode) => mode switch
    {
        WheelShifterMode.Sequential => WheelShifterSequential,
        WheelShifterMode.HPattern   => WheelShifterHPattern,
        _                           => WheelShifterPaddlesOnly,
    };

    public static ControlDefinition[] GetControlsForType(RawInputDeviceType type) => type switch
    {
        RawInputDeviceType.Gamepad  => Gamepad,
        RawInputDeviceType.Lightgun => Lightgun,
        RawInputDeviceType.Wheel    => Wheel,
        _                           => [],
    };
}
