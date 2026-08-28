using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UGL.GuideWatcher;

/// <summary>
/// Tiny, dependency-free background process: watches every connected XInput
/// controller for the Xbox/Guide button and launches (or re-foregrounds) UGL.exe
/// when it's pressed — even if UGL isn't running yet.
///
/// This has to be a separate process from UGL.exe itself. UGL's own controller
/// polling (see UGL.Input.XInputPollingService) can only react to input once UGL is
/// already running — there's nothing to catch a button press before that. A
/// dedicated watcher that starts with Windows and runs for the whole session is the
/// only way a controller button can "launch the app" the same way a keyboard
/// shortcut or desktop icon would.
///
/// The Guide button itself isn't part of the public XInput API — Microsoft
/// deliberately excludes it from XInputGetState's XINPUT_GAMEPAD.wButtons (it's
/// reserved for the OS/Steam overlay). Reading it requires the undocumented
/// XInputGetStateEx, exported by ORDINAL 100 only (no name) from xinput1_4.dll —
/// the same technique third-party tools like x360ce use. This is inherently
/// unsupported by Microsoft and could stop working in a future Windows update;
/// there's no documented alternative.
///
/// This process is never started automatically by UGL or by this build — the user
/// has to add it to their own Windows startup (Task Scheduler or the Startup
/// folder). See the README note next to this file / the release notes for how.
/// </summary>
internal static class Program
{
    private const int PollIntervalMs = 150;
    private const int MaxControllers = 4;
    private const ushort ButtonGuide = 0x0400; // undocumented XInputGetStateEx-only bit

    [STAThread]
    private static void Main()
    {
        // Refuse to run twice — a second instance would just double-launch UGL on
        // every Guide press. Not owning the mutex (someone else already does) means
        // exit quietly; there is no UI to show an error in.
        using var singleInstance = new Mutex(initiallyOwned: true, "UGL_GuideWatcher_SingleInstance", out bool createdNew);
        if (!createdNew) return;

        var exeDir = AppContext.BaseDirectory;
        var uglExePath = Path.Combine(exeDir, "UGL.exe");

        var prevGuideState = new bool[MaxControllers];

        while (true)
        {
            for (uint i = 0; i < MaxControllers; i++)
            {
                if (GetStateEx(i, out var state) != 0) continue; // controller not connected

                bool guideNow = (state.Gamepad.wButtons & ButtonGuide) != 0;
                bool guideWas = prevGuideState[i];
                prevGuideState[i] = guideNow;

                if (guideNow && !guideWas)
                    OnGuidePressed(uglExePath);
            }

            Thread.Sleep(PollIntervalMs);
        }
    }

    /// <summary>Foreground the already-running UGL window, or launch a fresh one.</summary>
    private static void OnGuidePressed(string uglExePath)
    {
        try
        {
            var existing = Process.GetProcessesByName("UGL").FirstOrDefault(p => !p.HasExited);
            var hwnd = existing is not null ? FindTopLevelWindowForProcess((uint)existing.Id) : 0;
            if (hwnd != 0)
            {
                ForceForegroundWindow(hwnd);
                return;
            }

            if (existing is not null) return; // UGL is running but no window found yet — don't double-launch

            if (File.Exists(uglExePath))
                Process.Start(new ProcessStartInfo(uglExePath) { UseShellExecute = true });
        }
        catch
        {
            // No console/UI to report to, and nothing useful to retry — next Guide
            // press tries again from scratch regardless of what failed this time.
        }
    }

    /// <summary>
    /// Finds UGL's real top-level window by scanning every top-level window on the
    /// desktop and matching by owning process id — NOT Process.MainWindowHandle,
    /// which is a well-documented unreliable .NET heuristic (see
    /// UGL.Emulators.WindowActivationNative's doc comment: "Process.MainWindowHandle
    /// was confirmed unreliable in real testing (zero window ever found...)" for the
    /// exact same reason this rewrite exists — a fullscreen Avalonia window is
    /// exactly the kind of window that heuristic can miss). "Real" here means
    /// visible and unowned (GW_OWNER == 0) — the same distinction that filters out
    /// tool/dialog windows elsewhere in UGL.
    /// </summary>
    private static nint FindTopLevelWindowForProcess(uint pid)
    {
        nint found = 0;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (windowPid != pid) return true; // keep enumerating
            if (!IsWindowVisible(hWnd)) return true;
            if (GetWindow(hWnd, GW_OWNER) != 0) return true;

            found = hWnd;
            return false; // stop — found it
        }, 0);
        return found;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    private const uint GW_OWNER = 4;

    // ── XInputGetStateEx (undocumented, ordinal 100) ──────────────────────────

    [DllImport("xinput1_4.dll", EntryPoint = "#100")]
    private static extern uint GetStateEx(uint dwUserIndex, out XInputState pState);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    // ── Foreground activation (same AttachThreadInput technique used elsewhere in
    // UGL — see UGL.Emulators.ProcessEmulatorLauncher.ForceForegroundWindow and
    // UGL.App.Views.WindowRestoreNative for the full rationale — duplicated here in
    // full rather than referenced, since this project deliberately takes no
    // ProjectReference on anything else). ──────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int SW_RESTORE = 9;

    private static void ForceForegroundWindow(nint hWnd)
    {
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
            SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
    }
}
