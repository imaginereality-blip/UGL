using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;
using UGL.Hooks;

namespace UGL.Emulators;

/// <summary>
/// Production IEmulatorLauncher implementation.
///
/// Launch sequence:
///   1. Resolve Emulator definition from IEmulatorRepository by game.EmulatorId
///   2. Build CLI arguments — substitute {rom} with the resolved ROM path
///   3. Spawn the process
///   4. Raise EmulatorExited when the process terminates
///
/// Window management (minimize/restore) is handled by MainWindowViewModel
/// in response to the EmulatorExited event, keeping this class focused
/// purely on process lifecycle.
/// </summary>
public sealed class ProcessEmulatorLauncher : IEmulatorLauncher, IDisposable
{
    private readonly IEmulatorRepository _emulatorRepo;
    private readonly IRetroArchConfigGenerator _retroArchGenerator;
    private readonly IHookLauncher _hookLauncher;
    private readonly IConfigurationService _config;
    private readonly IRawInputService _rawInputService;
    private readonly IDisplayModeService _displayModeService;
    private readonly IHidHideService _hidHideService;
    private readonly ILogger<ProcessEmulatorLauncher> _logger;

    private Process? _currentProcess;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // ── Direct-launch hand-off tracking (Steam/Epic/GOG-style protocol launches) ──
    private static readonly TimeSpan ProcessNameOverridePollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProcessNameOverrideTimeout      = TimeSpan.FromSeconds(60);

    // ── Foreground window activation ──────────────────────────────────────────
    private static readonly TimeSpan WindowActivationPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan WindowActivationTimeout      = TimeSpan.FromSeconds(45);

    public event EventHandler<int>? EmulatorExited;

    public bool IsEmulatorRunning =>
        _currentProcess is not null && !_currentProcess.HasExited;

    public ProcessEmulatorLauncher(
        IEmulatorRepository emulatorRepo,
        IRetroArchConfigGenerator retroArchGenerator,
        IHookLauncher hookLauncher,
        IConfigurationService config,
        IRawInputService rawInputService,
        IDisplayModeService displayModeService,
        IHidHideService hidHideService,
        ILogger<ProcessEmulatorLauncher> logger)
    {
        _emulatorRepo = emulatorRepo;
        _retroArchGenerator = retroArchGenerator;
        _hookLauncher = hookLauncher;
        _config = config;
        _rawInputService = rawInputService;
        _displayModeService = displayModeService;
        _hidHideService = hidHideService;
        _logger = logger;
    }

    public async Task<int> LaunchGameAsync(Game game, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (IsEmulatorRunning)
            {
                _logger.LogWarning(
                    "Launch requested but emulator already running (PID {Pid}). Ignoring.",
                    _currentProcess!.Id);
                return -1;
            }

            // Resolve emulator definition
            var emulator = await _emulatorRepo.GetByIdAsync(game.EmulatorId, ct);
            if (emulator is null)
            {
                _logger.LogError(
                    "No emulator definition found for Id '{EmulatorId}' (game: {Title}).",
                    game.EmulatorId, game.Title);
                return -1;
            }

            // Warn about any missing BIOS file before launching — a game-level
            // override takes priority over the emulator's own default list when
            // present, matching the same "game overrides emulator" reasoning used
            // for BIOS everywhere else. This never blocks the launch: the emulator
            // itself is the actual authority on whether a given BIOS is required,
            // and some games/cores work fine without one.
            CheckBiosFiles(game, emulator);

            // ── Build ProcessStartInfo ─────────────────────────────────────
            ProcessStartInfo psi;

            if (emulator.IsRetroArchCore)
            {
                psi = await BuildRetroArchStartInfoAsync(game, emulator, ct);
            }
            else if (emulator.IsDirectLaunch)
            {
                psi = BuildDirectLaunchStartInfo(game, emulator);
            }
            else
            {
                var exePath = ResolveExecutablePath(emulator.ExecutablePath);
                if (!File.Exists(exePath))
                {
                    _logger.LogError(
                        "Emulator executable not found: {ExePath} (emulator: {Name})",
                        exePath, emulator.Name);
                    return -1;
                }

                var romPath = ResolveRomPath(game.RomPath);

                // Windows' command-line tokenizer splits ProcessStartInfo.Arguments on
                // whitespace, so an unquoted path containing a space (very common —
                // "Crazy Taxi 2 (USA).chd") gets split into several garbage arguments
                // that the emulator silently ignores, launching to its own home menu
                // instead of the game. Quote the substituted path when it needs it,
                // unless the template already quotes the token itself (avoids
                // double-quoting "{rom}"-style templates).
                var templateAlreadyQuotesToken = emulator.Arguments.Contains("\"{rom}\"", StringComparison.OrdinalIgnoreCase);
                var substitutedRomPath = !templateAlreadyQuotesToken && romPath.Contains(' ')
                    ? $"\"{romPath}\""
                    : romPath;
                var args = emulator.Arguments.Replace("{rom}", substitutedRomPath, StringComparison.OrdinalIgnoreCase);

                psi = new ProcessStartInfo
                {
                    FileName         = exePath,
                    Arguments        = args,
                    UseShellExecute  = false,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                };
            }

            // Launch the configured output-hook tool (MameHooker / Hook of the Reaper),
            // if enabled, before the emulator itself — it needs to already be listening
            // on the output port by the time the emulator's first signal fires. This is
            // a no-op if hook integration is disabled or not configured for this system.
            //
            // Deliberately placed here — after psi is built and all the file-existence
            // checks above have passed — rather than earlier: several of those checks
            // return -1 (or BuildRetroArchStartInfoAsync throws) before the emulator
            // actually starts, and starting the hook tool any earlier would leave it
            // running in the background with no emulator ever launching on a failed attempt.
            await _hookLauncher.StartAsync(game, ct);

            // A ProcessNameOverride means the process UGL is about to start is a
            // hand-off launcher (Steam/Epic/GOG Galaxy) rather than the game itself —
            // capture what's already running under that name *before* starting it, so
            // the hand-off tracker below can tell a pre-existing instance apart from
            // the one this launch actually spawns.
            bool hasOverride = !string.IsNullOrWhiteSpace(game.ProcessNameOverride);
            HashSet<int> preExistingPids = hasOverride
                ? Process.GetProcessesByName(game.ProcessNameOverride).Select(p => p.Id).ToHashSet()
                : [];

            _currentProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // When no override is configured, the process we're about to start IS the
            // game — track its exit directly, same as always. When an override is
            // configured, this process is just the hand-off launcher: don't wire its
            // exit as "the game ended" (it may exit in well under a second once it's
            // messaged an already-running Steam/Epic client), and don't count its own
            // launch here as an activation of it that TrackProcessNameOverrideAsync
            // should exclude — see there for the fallback if no hand-off ever appears.
            if (!hasOverride)
                _currentProcess.Exited += OnProcessExited;

            if (!_currentProcess.Start())
            {
                _logger.LogError("Process.Start() returned false for {Exe}", psi.FileName);
                _currentProcess.Dispose();
                _currentProcess = null;
                await _hookLauncher.StopAsync(); // don't leave it running with no emulator
                return -1;
            }

            var pid = _currentProcess.Id;
            _logger.LogInformation("Emulator started (PID {Pid}).", pid);

            // Disable whichever peripheral types this game specifies (e.g. Lightgun
            // and Wheel for a fighting game) so they can't interfere while it's
            // running. Placed here — only after a confirmed successful start —
            // rather than earlier, so a failed launch attempt (missing exe, bad
            // RetroArch config, Process.Start() returning false) can never leave
            // peripherals disabled with nothing around to re-enable them. Cleared
            // back to none in OnProcessExited.
            _rawInputService.SetDisabledDeviceTypes(game.DisabledDeviceTypes.ToHashSet());

            // The line above only silences UGL's own event pipeline — it has no effect
            // on what the emulator/game process itself reads, since it talks to Windows
            // directly. HidHide is the real, OS-level version. A no-op if HidHide isn't
            // configured, or nothing relevant is currently connected.
            await ApplyHidHideForGameAsync(game, ct);

            // Same "only after a confirmed successful start" placement as the peripheral
            // disable above — a failed launch attempt must never leave the display in a
            // switched state with nothing around to restore it. Game override takes
            // priority over the system's default, same "game overrides system" convention
            // used for BIOS and bezels.
            var effectiveDisplayMode = game.DisplayModeOverride
                ?? (await ResolveSystemAsync(game.SystemId))?.DisplayMode;
            _displayModeService.Apply(effectiveDisplayMode);

            // Bring the game's own window to the foreground. Windows only grants
            // SetForegroundWindow to whatever the user most recently interacted with —
            // a window that appears later (after emulator startup work, or after a
            // launcher protocol hands off to the real game process) is routinely
            // denied focus and just sits there until clicked manually, which breaks
            // controller-only operation entirely. AllowSetForegroundWindow pre-
            // authorizes this specific process; the polling loop then waits for its
            // main window to actually exist (often not immediate) and activates it.
            WindowActivationNative.AllowSetForegroundWindow((uint)pid);
            _ = ActivateWindowWhenReadyAsync(pid);

            if (hasOverride)
                _ = TrackProcessNameOverrideAsync(game, pid, preExistingPids);

            return pid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch game: {Title}", game.Title);
            await _hookLauncher.StopAsync(); // safe no-op if it was never started
            return -1;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task KillCurrentEmulatorAsync()
    {
        await _lock.WaitAsync();
        try
        {
            // Captured into a local rather than re-reading the field below — Kill()
            // causes the process to exit almost immediately, and OnProcessExited (which
            // runs on its own ThreadPool callback thread, entirely outside this lock,
            // since Process.Exited fires independently of it) can null out and dispose
            // _currentProcess while this method is still awaiting WaitForExitAsync on
            // it. Using a local avoids racing against the field itself; the process
            // object being disposed out from under the await is still possible and is
            // still handled below.
            var process = _currentProcess;
            if (process is null || process.HasExited) return;

            _logger.LogInformation("Killing emulator process (PID {Pid}).", process.Id);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // Expected race: OnProcessExited already tore down the process object
            // (it had, in fact, just been killed) before WaitForExitAsync got to
            // observe it — not a real failure, so no warning-level noise for it.
            _logger.LogDebug("Process object was already torn down by OnProcessExited while awaiting exit — expected race, not an error.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error killing emulator process.");
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── RetroArch puppeteer ────────────────────────────────────────────────

    private async Task<ProcessStartInfo> BuildRetroArchStartInfoAsync(
        Game game, Emulator emulator, CancellationToken ct)
    {
        var ra = emulator.RetroArch
            ?? throw new InvalidOperationException(
                $"IsRetroArchCore=true but RetroArch config is null for '{emulator.Id}'.");

        var retroArchExe  = ResolveExecutablePath(ra.RetroArchExePath);
        var corePath      = ResolveExecutablePath(emulator.ExecutablePath);
        var romPath       = ResolveRomPath(game.RomPath);

        var system = await ResolveSystemAsync(game.SystemId)
            ?? new GameSystem { Id = game.SystemId, Name = game.SystemId }; // defensive fallback — no bezel, but never blocks launch

        var overrideCfg   = await _retroArchGenerator.GenerateAsync(game, emulator, system, ct);

        if (!File.Exists(retroArchExe))
            throw new FileNotFoundException($"RetroArch executable not found: {retroArchExe}");

        if (!File.Exists(corePath))
            throw new FileNotFoundException($"RetroArch core not found: {corePath}");

        // Build the RetroArch command line
        var args = $"-L \"{corePath}\" \"{romPath}\" --appendconfig \"{overrideCfg}\"";

        _logger.LogInformation(
            "Launching '{Title}' via RetroArch: {Exe} {Args}",
            game.Title, retroArchExe, args);

        return new ProcessStartInfo
        {
            FileName         = retroArchExe,
            Arguments        = args,
            UseShellExecute  = false,
            WorkingDirectory = Path.GetDirectoryName(retroArchExe) ?? AppContext.BaseDirectory,
        };
    }

    // ── Direct launch (native Windows game / Steam / Epic / GOG Galaxy) ──────

    /// <summary>
    /// Game.RomPath is either an absolute path to the game's own .exe, or a launcher
    /// protocol URI (e.g. "steam://rungameid/12345"). UseShellExecute=true is required
    /// for a protocol URI to resolve to its registered handler, and works fine for a
    /// plain .exe too, so it's used unconditionally here rather than branching on shape.
    /// </summary>
    private static ProcessStartInfo BuildDirectLaunchStartInfo(Game game, Emulator emulator)
    {
        var target = game.RomPath;
        bool isUri = Uri.TryCreate(target, UriKind.Absolute, out var parsed) && !parsed.IsFile;
        var resolvedTarget = isUri ? target : UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(target);

        return new ProcessStartInfo
        {
            FileName         = resolvedTarget,
            Arguments        = isUri ? string.Empty : emulator.Arguments, // literal, no {rom} token — a URI already encodes everything
            UseShellExecute  = true,
            WorkingDirectory = isUri ? string.Empty : (Path.GetDirectoryName(resolvedTarget) ?? AppContext.BaseDirectory),
        };
    }

    /// <summary>
    /// Polls for a new process named <see cref="Game.ProcessNameOverride"/> to appear
    /// after a direct-launch hand-off (Steam/Epic/GOG Galaxy), then swaps the tracked
    /// process over to it so the real game's exit — not the launcher's — ends the
    /// session. If nothing matches within <see cref="ProcessNameOverrideTimeout"/>, falls
    /// back to treating the original launcher process's own exit as the session end,
    /// so a misconfigured override can't leave UGL minimized forever.
    /// </summary>
    private async Task TrackProcessNameOverrideAsync(Game game, int wrapperPid, HashSet<int> excludePids)
    {
        var deadline = DateTime.UtcNow + ProcessNameOverrideTimeout;
        Process? matched = null;

        while (matched is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(ProcessNameOverridePollInterval);
            try
            {
                matched = Process.GetProcessesByName(game.ProcessNameOverride)
                    .FirstOrDefault(p => !excludePids.Contains(p.Id));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error polling for hand-off process '{Name}'.", game.ProcessNameOverride);
            }
        }

        await _lock.WaitAsync();
        try
        {
            // The session already moved on (killed, or a fresh launch replaced what we
            // were tracking) while we were polling — nothing left to swap onto.
            if (_currentProcess is null || _currentProcess.Id != wrapperPid)
            {
                matched?.Dispose();
                return;
            }

            var wrapper = _currentProcess;

            if (matched is not null)
            {
                _logger.LogInformation(
                    "Matched hand-off process '{Name}' (PID {Pid}) for '{Title}'.",
                    game.ProcessNameOverride, matched.Id, game.Title);
                _currentProcess = matched;
                matched.EnableRaisingEvents = true;
                matched.Exited += OnProcessExited;
            }
            else
            {
                _logger.LogWarning(
                    "Timed out waiting for process '{Name}' for '{Title}' — treating the " +
                    "launcher process's own exit as the session end instead.",
                    game.ProcessNameOverride, game.Title);
                wrapper.EnableRaisingEvents = true;
                wrapper.Exited += OnProcessExited;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = -1;
        try { exitCode = _currentProcess?.ExitCode ?? -1; }
        catch { /* process already disposed */ }

        _logger.LogInformation("Emulator process exited (code {ExitCode}).", exitCode);

        // Clean up RetroArch override config if one was generated
        _retroArchGenerator.Cleanup();

        // Re-enable any peripheral types this game had disabled - back to the
        // normal "nothing disabled" state for whatever's browsed/launched next.
        _rawInputService.SetDisabledDeviceTypes(new HashSet<RawInputDeviceType>());

        // Restore the user's normal display mode if this session switched it — a safe
        // no-op if it never did.
        _displayModeService.Restore();

        // Un-hide any peripherals HidHide hid for this session — fire-and-forget for
        // the same reason as _hookLauncher.StopAsync() above (OnProcessExited is a
        // synchronous Process.Exited handler).
        _ = _hidHideService.RestoreAsync();

        // Fire-and-forget — OnProcessExited is a synchronous event handler (Process.Exited),
        // so it can't await this directly. StopAsync() is safe to call even if hook
        // integration was never started (no-ops if there's nothing running).
        _ = _hookLauncher.StopAsync();

        EmulatorExited?.Invoke(this, exitCode);
        _currentProcess?.Dispose();
        _currentProcess = null;
    }

    /// <summary>
    /// Resolves an emulator executable path — relative paths (e.g.
    /// "emulators/mame/mame.exe") resolve against the app's own base directory;
    /// absolute paths are used as-is. Delegates to the same shared helper used
    /// everywhere else a stored path needs resolving back to a real file, so this
    /// stays consistent rather than each caller reimplementing the same two lines.
    /// </summary>
    /// <summary>
    /// Logs a warning for any configured BIOS file that isn't actually present.
    /// Game.BiosOverridePaths takes priority over Emulator.BiosPaths when non-empty —
    /// same "game overrides emulator" convention as everywhere else BIOS applies.
    /// </summary>
    private void CheckBiosFiles(Game game, Emulator emulator)
    {
        var required = game.BiosOverridePaths.Count > 0 ? game.BiosOverridePaths : emulator.BiosPaths;
        foreach (var bios in required)
        {
            var resolved = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(bios);
            if (!File.Exists(resolved))
                _logger.LogWarning(
                    "BIOS file not found: {Path} (required by {Source} for '{Title}'). " +
                    "The emulator may fail to start or run incorrectly without it.",
                    resolved, game.BiosOverridePaths.Count > 0 ? "game override" : emulator.Name, game.Title);
        }
    }

    /// <summary>Device types RawInputService/HidHide ever act on for this purpose —
    /// same fixed set the Game editor's checkbox grid uses (not Keyboard/Mouse/Unknown).</summary>
    private static readonly RawInputDeviceType[] AssignablePeripheralTypes =
    [
        RawInputDeviceType.Gamepad, RawInputDeviceType.Lightgun,
        RawInputDeviceType.Wheel, RawInputDeviceType.Spinner, RawInputDeviceType.Trackball,
    ];

    /// <summary>
    /// Resolves which connected devices should be hidden from this game via HidHide.
    /// PlayerDeviceAssignments (specific, ranked, per-player-slot picks) takes over
    /// entirely when non-empty — DisabledDeviceTypes is ignored for that game, since
    /// the assignment list already implies exactly which devices should stay visible
    /// (hide every assignable-type device not selected for any slot). Otherwise falls
    /// back to the simpler "hide everything of these types" behavior.
    /// </summary>
    private async Task ApplyHidHideForGameAsync(Game game, CancellationToken ct)
    {
        var connected = _rawInputService.EnumerateDevices();
        List<string> idsToHide;

        if (game.PlayerDeviceAssignments.Count > 0)
        {
            var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in game.PlayerDeviceAssignments)
            {
                var chosen = slot.PreferredHardwarePaths
                    .FirstOrDefault(path => connected.Any(d => string.Equals(d.HardwarePath, path, StringComparison.OrdinalIgnoreCase)));
                if (chosen is not null) selectedPaths.Add(chosen);
            }

            idsToHide = connected
                .Where(d => AssignablePeripheralTypes.Contains(d.DeviceType) && !selectedPaths.Contains(d.HardwarePath))
                .Select(d => _rawInputService.ResolveDeviceInstanceId(d.HardwarePath))
                .Where(id => id is not null)
                .Select(id => id!)
                .ToList();
        }
        else if (game.DisabledDeviceTypes.Count > 0)
        {
            idsToHide = connected
                .Where(d => game.DisabledDeviceTypes.Contains(d.DeviceType))
                .Select(d => _rawInputService.ResolveDeviceInstanceId(d.HardwarePath))
                .Where(id => id is not null)
                .Select(id => id!)
                .ToList();
        }
        else
        {
            return;
        }

        if (idsToHide.Count > 0)
            await _hidHideService.HideAsync(idsToHide, ct);
    }

    /// <summary>
    /// Polls for the launched game/emulator's real top-level window and brings it to
    /// the foreground. Deliberately does NOT rely on Process.MainWindowHandle for the
    /// launched process alone — confirmed unreliable in real testing (zero window
    /// ever found, for both a native emulator and a native Windows game, within the
    /// full timeout window). Instead scans the launched process's entire descendant
    /// tree (rescanned every tick, since a child render/launcher process can spawn
    /// well after the initial Process.Start()) and matches real top-level windows
    /// directly via EnumWindows — the same technique full-featured launchers use,
    /// and the only one that reliably covers a game whose visible window belongs to
    /// a child process rather than the one .NET's Process object represents.
    ///
    /// Keeps polling for the whole timeout window rather than stopping after the
    /// first attempt: a hand-off launcher commonly shows a short-lived splash/dialog
    /// window first whose handle differs from the eventual real game window, and a
    /// single SetForegroundWindow attempt can simply fail (observed in testing) and
    /// deserves a retry rather than being abandoned.
    /// </summary>
    private async Task ActivateWindowWhenReadyAsync(int launchedPid)
    {
        var deadline = DateTime.UtcNow + WindowActivationTimeout;
        var successfullyActivated = new HashSet<nint>();
        bool everFoundWindow = false;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(WindowActivationPollInterval);

            var process = _currentProcess;
            if (process is null || process.HasExited) return;

            var family = BuildProcessFamily(process.Id);
            var candidates = FindTopLevelWindowsForFamily(family);

            foreach (var handle in candidates)
            {
                everFoundWindow = true;
                // A *failed* attempt is deliberately not remembered, so the same
                // window keeps getting retried every tick until it succeeds or the
                // deadline passes, rather than being tried once and abandoned.
                if (successfullyActivated.Contains(handle)) continue;

                bool ok = ForceForegroundWindow(handle);
                _logger.LogInformation("Activated game window (PID {Pid}, hwnd={Handle}, success={Ok}).",
                    process.Id, handle, ok);

                if (ok) successfullyActivated.Add(handle);
            }
        }

        if (!everFoundWindow)
            _logger.LogWarning(
                "No top-level window ever appeared for PID {Pid} (or its child processes) within {Timeout}s — could not bring it to the foreground.",
                launchedPid, WindowActivationTimeout.TotalSeconds);
    }

    /// <summary>Every process ID descended from rootPid (rootPid included), via a
    /// full process-table snapshot walked top-down from the root.</summary>
    private static HashSet<uint> BuildProcessFamily(int rootPid)
    {
        var family = new HashSet<uint> { (uint)rootPid };
        var childrenByParent = new Dictionary<uint, List<uint>>();

        nint snapshot = WindowActivationNative.CreateToolhelp32Snapshot(WindowActivationNative.TH32CS_SNAPPROCESS, 0);
        if (snapshot == WindowActivationNative.InvalidHandleValue) return family;

        try
        {
            var entry = new WindowActivationNative.PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<WindowActivationNative.PROCESSENTRY32>(),
            };

            if (WindowActivationNative.Process32First(snapshot, ref entry))
            {
                do
                {
                    if (!childrenByParent.TryGetValue(entry.th32ParentProcessID, out var list))
                        childrenByParent[entry.th32ParentProcessID] = list = [];
                    list.Add(entry.th32ProcessID);
                } while (WindowActivationNative.Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            WindowActivationNative.CloseHandle(snapshot);
        }

        var queue = new Queue<uint>();
        queue.Enqueue((uint)rootPid);
        while (queue.Count > 0)
        {
            if (!childrenByParent.TryGetValue(queue.Dequeue(), out var children)) continue;
            foreach (var child in children)
                if (family.Add(child)) queue.Enqueue(child);
        }

        return family;
    }

    /// <summary>Real (visible, unowned, titled) top-level windows belonging to any
    /// process in the given family.</summary>
    private static List<nint> FindTopLevelWindowsForFamily(HashSet<uint> family)
    {
        var results = new List<nint>();

        WindowActivationNative.EnumWindows((hwnd, _) =>
        {
            if (!WindowActivationNative.IsWindowVisible(hwnd)) return true;
            if (WindowActivationNative.GetWindow(hwnd, WindowActivationNative.GW_OWNER) != 0) return true;

            WindowActivationNative.GetWindowThreadProcessId(hwnd, out var pid);
            if (!family.Contains(pid)) return true;

            if (WindowActivationNative.GetWindowTextLength(hwnd) == 0) return true;

            results.Add(hwnd);
            return true;
        }, 0);

        return results;
    }

    /// <summary>
    /// AttachThreadInput-based foreground activation — a plain SetForegroundWindow
    /// call (even preceded by AllowSetForegroundWindow) was confirmed insufficient in
    /// real testing (returned false against a genuine emulator window). Temporarily
    /// attaching UGL's calling thread's input state to whichever thread currently
    /// owns the foreground window shares that thread's permission to change the
    /// foreground window for the duration of the attachment — the same mechanism
    /// used internally by Explorer/the taskbar for this exact scenario.
    /// </summary>
    private static bool ForceForegroundWindow(nint hWnd)
    {
        uint currentThreadId = WindowActivationNative.GetCurrentThreadId();
        nint currentForeground = WindowActivationNative.GetForegroundWindow();
        uint foregroundThreadId = WindowActivationNative.GetWindowThreadProcessId(currentForeground, out _);

        bool attached = foregroundThreadId != 0
            && foregroundThreadId != currentThreadId
            && WindowActivationNative.AttachThreadInput(currentThreadId, foregroundThreadId, true);

        try
        {
            if (WindowActivationNative.IsIconic(hWnd))
                WindowActivationNative.ShowWindow(hWnd, WindowActivationNative.SW_RESTORE);
            WindowActivationNative.BringWindowToTop(hWnd);
            return WindowActivationNative.SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached)
                WindowActivationNative.AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
    }

    private async Task<GameSystem?> ResolveSystemAsync(string systemId)
    {
        var systems = await _config.GetSystemsAsync();
        return systems.FirstOrDefault(s => string.Equals(s.Id, systemId, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveExecutablePath(string path) =>
        UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(path);

    /// <summary>Resolves a ROM path — same reasoning as ResolveExecutablePath above.</summary>
    private static string ResolveRomPath(string path) =>
        UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(path);

    public void Dispose()
    {
        _currentProcess?.Dispose();
        _lock.Dispose();
    }
}
