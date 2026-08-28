namespace UGL.Core.Models;

/// <summary>
/// Global, per-physical-device controller configuration — replaces having to set
/// button mapping/sensitivity up separately for every game. Keyed by HardwarePath
/// (the same stable RawInput identifier PeripheralRegistry uses), not just
/// DeviceType, so two controllers of the same type can be configured differently.
///
/// Scope (see category_card_title_overlay-adjacent planning discussion — this
/// configures UGL's own menu navigation and RetroArch-launched emulation only.
/// Windows delivers controller input directly to native/Steam games once launched;
/// UGL is not in that path and can't remap it without a virtual-controller driver,
/// which is out of scope for now):
///   - Gamepad: which physical XInput button fires which menu ControllerAction,
///     stick dead zone, and menu-navigation repeat timing.
///   - Lightgun: a default DemulShooterTarget (a game's own field still overrides
///     this when set — same "game overrides default" convention as
///     Game.BiosOverridePaths/BezelOverridePath).
///   - Wheel: analog dead zone/sensitivity fed into RetroArch's own binds for
///     wheel-compatible cores, and whether force feedback is enabled.
/// </summary>
public sealed class ControllerProfile
{
    public required string HardwarePath { get; set; }
    public RawInputDeviceType DeviceType { get; set; }

    /// <summary>Denormalized so the Controllers tab can still show a sensible name
    /// for a profile whose device isn't currently connected.</summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>Distinguishes multiple named, saved profiles for the SAME physical
    /// device (Save As / Load in the Controllers tab) — the repository key is really
    /// (HardwarePath, ProfileName), not HardwarePath alone. "Default" for a device's
    /// first/only profile.</summary>
    public string ProfileName { get; set; } = "Default";

    /// <summary>True for exactly one profile per HardwarePath at a time — the one
    /// XInputPollingService/RetroArch config generation actually apply. Switched by
    /// Load without touching any profile's own contents.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Generic physical-control remap table: ControlId (from
    /// ControllerControlDefinitions, e.g. "A", "LeftTrigger", "GasPedal") → the
    /// physical input it's currently bound to (from ButtonBindingPool/AxisBindingPool,
    /// e.g. "LB", "RTAxis"). Unlike ButtonMap below (which remaps UGL's own menu
    /// actions), this is device-surface-first — "what does the A control respond to
    /// right now" — and applies as a translation layer in front of the fixed
    /// menu-action dispatch for Gamepad, or feeds RetroArch config generation for
    /// Lightgun/Wheel. A ControlId absent here uses its ControlDefinition's own
    /// DefaultBinding, so a partially-configured profile is always safe.
    /// </summary>
    public Dictionary<string, string> ControlBindings { get; set; } = new();

    /// <summary>Wheel only — which shifter control set is active (see
    /// ControllerControlDefinitions.WheelShifterMode). Ignored for other device types.</summary>
    public string ShifterMode { get; set; } = "Sequential";

    // ── Gamepad ────────────────────────────────────────────────────────────────
    // Only the ControllerActions a user has actually remapped need an entry —
    // anything absent falls back to XInputPollingService's existing hardcoded
    // default mapping, so an unconfigured/partially-configured profile is always
    // safe to use.
    public Dictionary<ControllerAction, ushort> ButtonMap { get; set; } = new();
    public int ThumbDeadZone { get; set; } = 8689; // matches XInput.ThumbDeadZone's current default
    public int MenuRepeatDelayMs { get; set; } = 400;
    public int MenuRepeatIntervalMs { get; set; } = 120;

    // ── Lightgun ───────────────────────────────────────────────────────────────
    public string DemulShooterTarget { get; set; } = string.Empty;

    // ── Wheel ──────────────────────────────────────────────────────────────────
    public double AxisDeadZone { get; set; } = 0.1;
    public double AxisSensitivity { get; set; } = 1.0;
    public bool ForceFeedbackEnabled { get; set; } = true;
}
