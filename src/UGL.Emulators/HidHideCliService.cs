using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;

namespace UGL.Emulators;

/// <summary>
/// Production IHidHideService implementation — shells out to HidHideCLI.exe (the
/// officially supported control surface; HidHide's underlying IOCTL protocol to its
/// \\.\HidHide device is undocumented and version-fragile, so the CLI is used
/// instead of reimplementing it). Flags verified against HidHide's own CLI source
/// (HidHideCLI/src/Commands.cpp) rather than assumed: --app-reg, --dev-hide,
/// --dev-unhide, --cloak-on all take a single argument (a fully-qualified path or a
/// device instance ID) and are idempotent — safe to call repeatedly.
/// </summary>
public sealed class HidHideCliService : IHidHideService
{
    private readonly IConfigurationService _config;
    private readonly ILogger<HidHideCliService> _logger;
    private readonly List<string> _hiddenIds = [];
    private bool _uglRegistered;

    public HidHideCliService(IConfigurationService config, ILogger<HidHideCliService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsAvailable
    {
        get
        {
            var settings = _config.Settings;
            if (!settings.HidHideEnabled || string.IsNullOrWhiteSpace(settings.HidHideCliPath)) return false;
            return File.Exists(UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(settings.HidHideCliPath));
        }
    }

    public async Task<bool> HideAsync(IReadOnlyCollection<string> deviceInstanceIds, CancellationToken ct = default)
    {
        if (deviceInstanceIds.Count == 0) return true;
        if (!IsAvailable)
        {
            _logger.LogWarning("HidHide not configured/enabled — {Count} peripheral(s) will remain visible to the game.",
                deviceInstanceIds.Count);
            return false;
        }

        var cliPath = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(_config.Settings.HidHideCliPath);

        // UGL itself must stay on the allow-list, or it would lose sight of its own
        // devices (Controllers tab, menu-navigation gamepad) the moment cloaking is
        // active. Registering and activating cloaking are both idempotent, so it's
        // safe to redo this on every launch rather than tracking whether it's "already
        // done" across app restarts.
        if (!_uglRegistered)
        {
            var uglExePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(uglExePath))
                await RunCliAsync(cliPath, $"--app-reg \"{uglExePath}\"", ct);
            _uglRegistered = true;
        }
        await RunCliAsync(cliPath, "--cloak-on", ct);

        bool allOk = true;
        foreach (var id in deviceInstanceIds)
        {
            var ok = await RunCliAsync(cliPath, $"--dev-hide \"{id}\"", ct);
            if (ok) _hiddenIds.Add(id);
            else allOk = false;
        }

        _logger.LogInformation("HidHide: hid {Count} device(s) from all processes except UGL.", _hiddenIds.Count);
        return allOk;
    }

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        if (_hiddenIds.Count == 0) return;
        if (!IsAvailable) return; // nothing we could have hidden without it being available

        var cliPath = UGL.Core.Utilities.PortablePathHelper.ToAbsolutePath(_config.Settings.HidHideCliPath);

        foreach (var id in _hiddenIds)
            await RunCliAsync(cliPath, $"--dev-unhide \"{id}\"", ct);

        _logger.LogInformation("HidHide: restored {Count} device(s).", _hiddenIds.Count);
        _hiddenIds.Clear();
    }

    private async Task<bool> RunCliAsync(string cliPath, string arguments, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(psi);
            if (process is null) return false;

            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(ct);
                _logger.LogWarning("HidHideCLI '{Args}' exited with code {Code}: {Error}",
                    arguments, process.ExitCode, stderr);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run HidHideCLI with arguments '{Args}'.", arguments);
            return false;
        }
    }
}
