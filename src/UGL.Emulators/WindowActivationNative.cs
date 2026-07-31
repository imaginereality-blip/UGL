using System.Runtime.InteropServices;

namespace UGL.Emulators;

/// <summary>
/// Minimal P/Invoke surface for bringing a just-launched game/emulator window to the
/// foreground. Windows normally only grants SetForegroundWindow to whichever process
/// the user just interacted with — a window that appears later (after an emulator's
/// own startup work, or after a launcher protocol hands off to the real game process)
/// routinely misses that window and is silently denied focus, which is exactly the
/// "had to click it with the mouse" failure mode this exists to fix. AllowSetForegroundWindow
/// pre-authorizes a specific process to succeed at its own SetForegroundWindow call, and
/// is also the sanctioned way for a *different* process (UGL) to call SetForegroundWindow
/// on that process's window without itself being subject to the same restriction.
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

    internal const int SW_RESTORE = 9;
}
