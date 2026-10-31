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

        bool ok = true;

        // Resolution first: the set of valid scale steps (and which one Windows
        // recommends) depends on the active resolution.
        if (mode.Width is not null || mode.Height is not null || mode.RefreshHz is not null)
            ok = ApplyResolution(mode);

        if (mode.ScalePercent is not null)
            ok &= ApplyScale(mode.ScalePercent.Value);

        return ok;
    }

    private bool ApplyResolution(DisplayMode mode)
    {
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
    /// Called unconditionally on every emulator exit, regardless of whether Apply()
    /// was ever called on this instance — many emulators (redream, RetroArch's own
    /// fullscreen-exclusive video drivers, etc.) switch the actual display
    /// resolution themselves, entirely outside UGL's own DisplayMode config; killing
    /// that process abruptly (the quit-to-launcher combo uses Process.Kill, not a
    /// graceful close) skips whatever cleanup the emulator would otherwise have done
    /// on its own, and nothing else in Windows restores the mode automatically.
    ///
    /// Actually comparing current vs. the registry-stored mode first — rather than
    /// always issuing the ChangeDisplaySettingsEx call — matters in practice: a
    /// monitor mode switch is a real hardware resync (commonly a second or more of
    /// black screen while the display re-locks), and most games/exits never
    /// actually touched the resolution at all (a windowed or borderless game, for
    /// instance). Firing the resync unconditionally on every single exit was a real,
    /// reported regression — it made returning from *any* game feel slow, not just
    /// the ones that actually changed the mode.
    /// </summary>
    public void Restore()
    {
        RestoreResolution();

        // After the resolution restore, since the valid scale steps depend on it.
        RestoreScale();
    }

    private void RestoreResolution()
    {
        if (ModeAlreadyMatchesRegistryDefault())
        {
            _logger.LogDebug("Display already at the normal desktop mode — nothing to restore.");
            return;
        }

        int result = DisplayModeNative.ChangeDisplaySettingsExToDefault(
            null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

        if (result != DisplayModeNative.DISP_CHANGE_SUCCESSFUL)
            _logger.LogWarning("ChangeDisplaySettingsEx (restore to default) failed (code {Code}).", result);
        else
            _logger.LogInformation("Restored display mode to the user's normal desktop settings.");
    }

    // ── DPI scaling ───────────────────────────────────────────────────────

    /// <summary>Scale percent in effect before ApplyScale changed it; null when this
    /// session never changed scaling (so Restore has nothing to undo).</summary>
    private int? _originalScalePercent;

    private bool ApplyScale(int percent)
    {
        if (!TryGetPrimaryScale(out var source, out var info, out var currentPercent))
        {
            _logger.LogWarning("Could not read the display scale — skipping scale change.");
            return false;
        }

        if (currentPercent == percent) return true; // already there; nothing to restore either

        if (!TrySetScale(source, info, percent))
            return false;

        // Only remember the first original, so a repeated Apply can't overwrite the
        // user's real scaling with an intermediate one.
        _originalScalePercent ??= currentPercent;
        _logger.LogInformation("Switched display scaling from {From}% to {To}%.", currentPercent, percent);
        return true;
    }

    private void RestoreScale()
    {
        if (_originalScalePercent is not int original) return;
        _originalScalePercent = null;

        if (!TryGetPrimaryScale(out var source, out var info, out var currentPercent))
        {
            _logger.LogWarning("Could not read the display scale — leaving scaling as is.");
            return;
        }

        if (currentPercent != original && TrySetScale(source, info, original))
            _logger.LogInformation("Restored display scaling to {Percent}%.", original);
    }

    /// <summary>Finds the first active display source and reads its scaling range. The
    /// first active path is the primary display in the common single-monitor setup.</summary>
    private static bool TryGetPrimaryScale(out (int adapterLow, int adapterHigh, uint id) source, out DpiScaleGet info, out int currentPercent)
    {
        source = default;
        info = default;
        currentPercent = 0;

        if (DisplayModeNative.GetDisplayConfigBufferSizes(DisplayModeNative.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0
            || pathCount == 0)
            return false;

        var paths = new byte[pathCount * DisplayModeNative.PathInfoSize];
        var modes = new byte[modeCount * DisplayModeNative.ModeInfoSize];
        if (DisplayModeNative.QueryDisplayConfig(DisplayModeNative.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0
            || pathCount == 0)
            return false;

        // sourceInfo is the start of DISPLAYCONFIG_PATH_INFO: adapterId (LUID: low, high), id.
        source = (BitConverter.ToInt32(paths, 0), BitConverter.ToInt32(paths, 4), BitConverter.ToUInt32(paths, 8));

        info = new DpiScaleGet
        {
            type = DisplayModeNative.DEVICE_INFO_GET_DPI_SCALE,
            size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DpiScaleGet>(),
            adapterIdLow = source.adapterLow,
            adapterIdHigh = source.adapterHigh,
            id = source.id,
        };
        if (DisplayModeNative.DisplayConfigGetDeviceInfo(ref info) != 0) return false;

        // The API reports offsets from the recommended step, with minScaleRel at list index 0.
        int index = info.curScaleRel - info.minScaleRel;
        if (index < 0 || index >= DisplayModeNative.DpiScaleSteps.Length) return false;

        currentPercent = DisplayModeNative.DpiScaleSteps[index];
        return true;
    }

    private bool TrySetScale((int adapterLow, int adapterHigh, uint id) source, DpiScaleGet info, int percent)
    {
        int index = Array.IndexOf(DisplayModeNative.DpiScaleSteps, percent);
        int rel = index + info.minScaleRel;

        if (index < 0 || rel < info.minScaleRel || rel > info.maxScaleRel)
        {
            _logger.LogWarning("{Percent}% isn't an available scale for this display at its current resolution.", percent);
            return false;
        }

        var set = new DpiScaleSet
        {
            type = DisplayModeNative.DEVICE_INFO_SET_DPI_SCALE,
            size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DpiScaleSet>(),
            adapterIdLow = source.adapterLow,
            adapterIdHigh = source.adapterHigh,
            id = source.id,
            scaleRel = rel,
        };

        int result = DisplayModeNative.DisplayConfigSetDeviceInfo(ref set);
        if (result != 0)
        {
            _logger.LogWarning("DisplayConfigSetDeviceInfo failed (code {Code}) setting {Percent}% scaling.", result, percent);
            return false;
        }
        return true;
    }

    private bool ModeAlreadyMatchesRegistryDefault()
    {
        var current = new DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
        var registryDefault = new DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };

        bool gotCurrent = DisplayModeNative.EnumDisplaySettings(null, DisplayModeNative.ENUM_CURRENT_SETTINGS, ref current);
        bool gotDefault = DisplayModeNative.EnumDisplaySettings(null, DisplayModeNative.ENUM_REGISTRY_SETTINGS, ref registryDefault);

        // If either query fails, fall through to actually attempting the restore
        // rather than silently assuming nothing changed.
        if (!gotCurrent || !gotDefault) return false;

        return current.dmPelsWidth == registryDefault.dmPelsWidth
            && current.dmPelsHeight == registryDefault.dmPelsHeight
            && current.dmDisplayFrequency == registryDefault.dmDisplayFrequency;
    }
}
