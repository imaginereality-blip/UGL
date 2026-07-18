using System.Diagnostics;
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
    private readonly ILogger<ProcessEmulatorLauncher> _logger;

    private Process? _currentProcess;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event EventHandler<int>? EmulatorExited;

    public bool IsEmulatorRunning =>
        _currentProcess is not null && !_currentProcess.HasExited;

    public ProcessEmulatorLauncher(
        IEmulatorRepository emulatorRepo,
        IRetroArchConfigGenerator retroArchGenerator,
        IHookLauncher hookLauncher,
        IConfigurationService config,
        ILogger<ProcessEmulatorLauncher> logger)
    {
        _emulatorRepo = emulatorRepo;
        _retroArchGenerator = retroArchGenerator;
        _hookLauncher = hookLauncher;
        _config = config;
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
                var args = emulator.Arguments.Replace("{rom}", romPath, StringComparison.OrdinalIgnoreCase);

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
            await _hookLauncher.StartAsync(game.SystemId, ct);

            _currentProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
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
            if (_currentProcess is null || _currentProcess.HasExited) return;

            _logger.LogInformation("Killing emulator process (PID {Pid}).", _currentProcess.Id);
            _currentProcess.Kill(entireProcessTree: true);
            await _currentProcess.WaitForExitAsync();
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

        var systems = await _config.GetSystemsAsync();
        var system  = systems.FirstOrDefault(s => string.Equals(s.Id, game.SystemId, StringComparison.OrdinalIgnoreCase))
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

    // ── Helpers ────────────────────────────────────────────────────────────

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = -1;
        try { exitCode = _currentProcess?.ExitCode ?? -1; }
        catch { /* process already disposed */ }

        _logger.LogInformation("Emulator process exited (code {ExitCode}).", exitCode);

        // Clean up RetroArch override config if one was generated
        _retroArchGenerator.Cleanup();

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
