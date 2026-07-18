namespace UGL.Core.Models;

/// <summary>
/// Which output-hook tool (if any) UGL should launch alongside the emulator.
/// Mutually exclusive — MameHooker and Hook of the Reaper both listen on the same
/// TCP/UDP ports for the emulator's output signals, so running both at once would
/// conflict rather than combine.
/// </summary>
public enum HookToolType
{
    None,
    MameHooker,
    HookOfTheReaper,
}

/// <summary>
/// Persisted to config/hooks.json. UGL's role here is deliberately limited to process
/// lifecycle — launch the configured tool (hidden, background) just before the
/// emulator starts, close it when the emulator exits. It listens on the network port
/// the emulator broadcasts to (the same MAME-established output-signal standard both
/// tools support) and resolves its own per-game config entirely on its own; UGL does
/// not parse, forward, or otherwise get involved in that traffic.
/// </summary>
public sealed class HookSettings
{
    public bool EnabledGlobally { get; set; } = false;
    public HookToolType ToolType { get; set; } = HookToolType.None;

    /// <summary>Full path to mamehook.exe or HookOfTheReaper.exe, whichever ToolType selects.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>System Ids to skip even when EnabledGlobally is true — e.g. systems with
    /// no light gun or output-signal games, where launching the tool would just be
    /// pointless background overhead.</summary>
    public List<string> DisabledForSystemIds { get; set; } = [];

    /// <summary>Milliseconds to wait after launching the hook tool before the emulator
    /// itself starts, so it's already listening on the output port in time. Some tools
    /// need a moment to initialize before the emulator's first output signal fires.</summary>
    public int StartupDelayMs { get; set; } = 500;
}
