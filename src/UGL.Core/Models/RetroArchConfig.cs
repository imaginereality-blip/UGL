namespace UGL.Core.Models;

/// <summary>
/// RetroArch-specific configuration for an emulator entry.
/// Populated when Emulator.IsRetroArchCore is true.
/// Values are written to ugl_override.cfg by RetroArchConfigGenerator.
/// </summary>
public sealed class RetroArchConfig
{
    /// <summary>Absolute or exe-relative path to the RetroArch executable.</summary>
    public string RetroArchExePath { get; set; } = string.Empty;

    /// <summary>Absolute or exe-relative path to the .dll core library file.</summary>
    public string CoreLibraryPath { get; set; } = string.Empty;

    /// <summary>Number of run-ahead frames (0 = disabled). Reduces input latency.</summary>
    public int RunAheadFrames { get; set; } = 0;

    /// <summary>Use second instance for run-ahead (reduces CPU cost, requires save states).</summary>
    public bool RunAheadSecondInstance { get; set; } = false;

    /// <summary>Path to a global CRT/shader preset file (.glslp / .slangp).</summary>
    public string ShaderPresetPath { get; set; } = string.Empty;

    /// <summary>Additional raw key=value lines appended verbatim to ugl_override.cfg.</summary>
    public string AdditionalConfig { get; set; } = string.Empty;
}
