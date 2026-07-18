using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Hooks;

/// <summary>
/// Manages the lifecycle of the configured output-hook tool (MameHooker or Hook of
/// the Reaper) — launch hidden/background just before the emulator starts, stop when
/// it exits. UGL does not communicate with the tool beyond this; it listens on the
/// network port the emulator broadcasts to and resolves its own per-game
/// configuration entirely on its own.
/// </summary>
public interface IHookLauncher
{
    bool IsRunning { get; }
    Task StartAsync(string systemId, CancellationToken ct = default);
    Task StopAsync();
}

public sealed class HookLauncher : IHookLauncher
{
    private readonly IHookSettingsRepository _hookSettings;
    private readonly ILogger<HookLauncher> _logger;
    private Process? _process;

    public HookLauncher(IHookSettingsRepository hookSettings, ILogger<HookLauncher> logger)
    {
        _hookSettings = hookSettings;
        _logger = logger;
    }

    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(string systemId, CancellationToken ct = default)
    {
        // Always start from a clean slate — if a previous hook process is somehow
        // still running (e.g. a prior StopAsync didn't fully clean up), don't leave two
        // instances fighting over the same output port.
        await StopAsync();

        var settings = await _hookSettings.GetSettingsAsync(ct);

        if (!settings.EnabledGlobally || settings.ToolType == HookToolType.None)
        {
            _logger.LogDebug("Hook integration disabled — skipping launch.");
            return;
        }

        if (settings.DisabledForSystemIds.Any(id => string.Equals(id, systemId, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug("Hook integration disabled for system {SystemId} — skipping launch.", systemId);
            return;
        }

        // Resolve a possibly-relative stored path against the app's own base
        // directory — using the raw stored value directly here would only have
        // worked by coincidence, if the process's current working directory
        // happened to already match the exe's folder (not guaranteed, e.g. when
        // launched via a shortcut with a different "Start in" value).
        var executablePath = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(settings.ExecutablePath);

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            _logger.LogWarning("Hook tool executable not found at '{Path}' — skipping launch.", executablePath);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            _process = Process.Start(psi);
            _logger.LogInformation("Launched hook tool: {Tool} ({Path})", settings.ToolType, executablePath);

            // Give it a moment to start listening on the output port before the
            // emulator itself launches and starts broadcasting signals.
            if (settings.StartupDelayMs > 0)
                await Task.Delay(settings.StartupDelayMs, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch hook tool at '{Path}'.", executablePath);
            _process = null;
        }
    }

    public Task StopAsync()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _logger.LogInformation("Hook tool process stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop hook tool process cleanly.");
            }
        }
        _process = null;
        return Task.CompletedTask;
    }
}
