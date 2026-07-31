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

    // ── Process-tree + top-level window enumeration ───────────────────────────
    // Process.MainWindowHandle was confirmed unreliable in real testing (zero
    // window ever found, for either a native emulator or a native Windows game, in
    // sessions that ran under its own 45s search window) — its heuristic covers only
    // windows owned by threads of the exact process handle .NET is holding, which
    // misses the common case of a game whose actual render window belongs to a
    // separate child process (launcher/anti-cheat wrapper, engine subprocess, etc.).
    // Scanning the full process tree and matching real top-level windows directly is
    // the same technique full-featured launchers use for this.

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    internal const uint TH32CS_SNAPPROCESS = 0x00000002;
    internal static readonly nint InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PROCESSENTRY32
    {
        internal uint dwSize;
        internal uint cntUsage;
        internal uint th32ProcessID;
        internal nint th32DefaultHeapID;
        internal uint th32ModuleID;
        internal uint cntThreads;
        internal uint th32ParentProcessID;
        internal int pcPriClassBase;
        internal uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string szExeFile;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool Process32First(nint hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool Process32Next(nint hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    internal static extern bool CloseHandle(nint hObject);

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport(DllName)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport(DllName)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport(DllName, CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint hWnd);

    [DllImport(DllName)]
    internal static extern nint GetWindow(nint hWnd, uint uCmd);

    // A top-level window with a non-null owner is a tool/dialog window, not a "real"
    // application window — the same heuristic distinction Process.MainWindowHandle's
    // own internal search uses.
    internal const uint GW_OWNER = 4;
}
