using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Emulators;

/// <summary>
/// Production IDisplayModeService implementation. Applies a temporary
/// (CDS_FULLSCREEN — not written to the registry) resolution/refresh-rate change via
/// ChangeDisplaySettingsExW, and restores the user's normal desktop settings by
/// reverting to the registry-stored mode rather than manually re-applying a captured
/// snapshot — simpler, and immune to ever drifting from whatever the user's actual
/// configured desktop mode is.
/// </summary>
public sealed class Win32DisplayModeService : IDisplayModeService
{
    private readonly ILogger<Win32DisplayModeService> _logger;

    public Win32DisplayModeService(ILogger<Win32DisplayModeService> logger)
    {
        _logger = logger;
    }

    public bool Apply(DisplayMode? mode)
    {
        if (mode is null || mode.IsEmpty) return true;

        var devMode = new DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
        if (!DisplayModeNative.EnumDisplaySettings(null, DisplayModeNative.ENUM_CURRENT_SETTINGS, ref devMode))
        {
            _logger.LogWarning("EnumDisplaySettings failed — cannot determine current display mode, skipping switch.");
            return false;
        }

        // Width/height are applied together (whichever wasn't specified keeps its
        // current value) since a mode change with only one of the pair set is not
        // meaningful to the display driver.
        int fields = 0;
        if (mode.Width is not null || mode.Height is not null)
        {
            devMode.dmPelsWidth  = mode.Width  ?? devMode.dmPelsWidth;
            devMode.dmPelsHeight = mode.Height ?? devMode.dmPelsHeight;
            fields |= DisplayModeNative.DM_PELSWIDTH | DisplayModeNative.DM_PELSHEIGHT;
        }
        if (mode.RefreshHz is not null)
        {
            devMode.dmDisplayFrequency = mode.RefreshHz.Value;
            fields |= DisplayModeNative.DM_DISPLAYFREQUENCY;
        }
        devMode.dmFields = fields;

        int result = DisplayModeNative.ChangeDisplaySettingsEx(
            null, ref devMode, IntPtr.Zero, DisplayModeNative.CDS_FULLSCREEN, IntPtr.Zero);

        if (result != DisplayModeNative.DISP_CHANGE_SUCCESSFUL)
        {
            _logger.LogWarning(
                "ChangeDisplaySettingsEx failed (code {Code}) switching to {Width}x{Height}@{Hz}Hz.",
                result, mode.Width, mode.Height, mode.RefreshHz);
            return false;
        }

        _logger.LogInformation("Switched display mode to {Width}x{Height}@{Hz}Hz.",
            mode.Width, mode.Height, mode.RefreshHz);
        return true;
    }

    /// <summary>
    /// Always attempts the revert, regardless of whether Apply() was ever called on
    /// this instance — not gated behind a "did we change it" flag. Many emulators
    /// (redream, RetroArch's own fullscreen-exclusive video drivers, etc.) switch the
    /// actual display resolution themselves, entirely outside UGL's own DisplayMode
    /// config; killing that process abruptly (the quit-to-launcher combo uses
    /// Process.Kill, not a graceful close) skips whatever cleanup the emulator would
    /// otherwise have done on its own, and nothing else in Windows restores the mode
    /// automatically. Reverting to the registry-stored mode unconditionally on every
    /// exit is a safe no-op when nothing actually changed.
    /// </summary>
    public void Restore()
    {
        int result = DisplayModeNative.ChangeDisplaySettingsExToDefault(
            null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

        if (result != DisplayModeNative.DISP_CHANGE_SUCCESSFUL)
            _logger.LogWarning("ChangeDisplaySettingsEx (restore to default) failed (code {Code}).", result);
        else
            _logger.LogInformation("Restored display mode to the user's normal desktop settings.");
    }
}
