using System.Runtime.InteropServices;

namespace UGL.App.Views;

/// <summary>
/// AttachThreadInput-based foreground activation for restoring UGL's OWN window after
/// an emulator/game exits. Mirrors UGL.Emulators.ProcessEmulatorLauncher's identical
/// technique for the opposite direction (activating a just-launched game window) —
/// see that class's ForceForegroundWindow for the full rationale. That launch-time
/// path was built specifically because a plain SetForegroundWindow/Activate() call was
/// confirmed insufficient in real testing; the exit-time restore path never got the
/// same hardening and used plain Activate()/Focus() instead, which is a strong
/// candidate for "doesn't always return to fullscreen" — whatever process owned the
/// foreground when the emulator process (or its window) was torn down may still hold
/// Windows' foreground-lock at the exact moment UGL tries to reclaim it.
/// </summary>
internal static class WindowRestoreNative
{
    private const string DllName = "user32.dll";

    [DllImport(DllName)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport(DllName)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport(DllName)]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport(DllName)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport(DllName)]
    private static extern nint GetForegroundWindow();

    [DllImport(DllName)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport(DllName)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int SW_RESTORE = 9;

    internal static bool ForceForegroundWindow(nint hWnd)
    {
        if (hWnd == 0) return false;

        uint currentThreadId = GetCurrentThreadId();
        nint currentForeground = GetForegroundWindow();
        uint foregroundThreadId = GetWindowThreadProcessId(currentForeground, out _);

        bool attached = foregroundThreadId != 0
            && foregroundThreadId != currentThreadId
            && AttachThreadInput(currentThreadId, foregroundThreadId, true);

        try
        {
            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);
            BringWindowToTop(hWnd);
            return SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
    }
}
