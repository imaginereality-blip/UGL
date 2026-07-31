using System.Runtime.InteropServices;

namespace UGL.Emulators;

/// <summary>
/// Minimal P/Invoke surface for bringing a just-launched game/emulator window to the
/// foreground. Windows normally only grants SetForegroundWindow to whichever process
/// currently owns (or was very recently granted) the right to change the foreground
/// window — a window that appears later (after an emulator's own startup work, or
/// after a launcher protocol hands off to the real game process) routinely gets
/// denied, which is exactly the "had to click it with the mouse" failure mode this
/// exists to fix.
///
/// AllowSetForegroundWindow alone is not sufficient here and was confirmed
/// insufficient in real testing (SetForegroundWindow returned false even after
/// calling it) — it grants the *target* process permission to foreground *itself*,
/// not UGL permission to foreground some other process's window on its behalf, which
/// is what's actually needed since UGL (not the game) is the one calling
/// SetForegroundWindow. The reliable technique is AttachThreadInput: temporarily
/// attaching UGL's input thread to whichever thread currently owns the foreground
/// window shares that thread's "may change the foreground window" state for the
/// duration of the attachment, which is the same mechanism the Windows shell itself
/// relies on for this.
/// </summary>
internal static class WindowActivationNative
{
    private const string DllName = "user32.dll";

    [DllImport(DllName)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport(DllName)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport(DllName)]
    internal static extern bool BringWindowToTop(nint hWnd);

    [DllImport(DllName)]
    internal static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport(DllName)]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport(DllName)]
    internal static extern nint GetForegroundWindow();

    [DllImport(DllName)]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport(DllName)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    internal const int SW_RESTORE = 9;
}
