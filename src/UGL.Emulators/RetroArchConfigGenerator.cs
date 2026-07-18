using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Emulators;

/// <summary>
/// Generates {exe}/retroarch/ugl_override.cfg from an Emulator's RetroArchConfig.
///
/// RetroArch is then launched as:
///   {RetroArchExePath} -L "{CoreLibraryPath}" "{romPath}"
///       --config "{userRetroarchCfg}"
///       --appendconfig "{ugl_override.cfg}"
///
/// The --appendconfig approach means UGL's settings layer on top of the
/// user's retroarch.cfg without modifying it. The override file is deleted
/// on emulator exit via Cleanup().
/// </summary>
public sealed class RetroArchConfigGenerator : IRetroArchConfigGenerator
{
    private readonly ILogger<RetroArchConfigGenerator> _logger;
    private readonly string _outputDir;
    private string? _lastGeneratedPath;
    private string? _lastGeneratedOverlayCfgPath;

    public RetroArchConfigGenerator(ILogger<RetroArchConfigGenerator> logger)
    {
        _logger = logger;
        _outputDir = Path.Combine(AppContext.BaseDirectory, "retroarch");
    }

    public async Task<string> GenerateAsync(
        Game game,
        Emulator emulator,
        GameSystem system,
        CancellationToken ct = default)
    {
        var ra = emulator.RetroArch
            ?? throw new InvalidOperationException(
                $"Emulator '{emulator.Id}' has IsRetroArchCore=true but no RetroArch config.");

        Directory.CreateDirectory(_outputDir);

        var outputPath = Path.Combine(_outputDir, "ugl_override.cfg");
        var romPath    = ResolvePath(game.RomPath);
        var corePath   = ResolvePath(ra.CoreLibraryPath);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# UGL RetroArch Override — auto-generated, do not edit manually");
        sb.AppendLine($"# Game:      {game.Title}");
        sb.AppendLine($"# Core:      {emulator.Name}");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // Core library path
        sb.AppendLine($"libretro_path = \"{EscapePath(corePath)}\"");

        // Run-ahead
        if (ra.RunAheadFrames > 0)
        {
            sb.AppendLine($"run_ahead_enabled = true");
            sb.AppendLine($"run_ahead_frames = {ra.RunAheadFrames}");
            sb.AppendLine($"run_ahead_secondary_instance = {ra.RunAheadSecondInstance.ToString().ToLower()}");
        }
        else
        {
            sb.AppendLine("run_ahead_enabled = false");
        }

        // Shader preset
        if (!string.IsNullOrWhiteSpace(ra.ShaderPresetPath))
        {
            var shaderPath = ResolvePath(ra.ShaderPresetPath);
            if (File.Exists(shaderPath))
            {
                sb.AppendLine($"video_shader_enable = true");
                sb.AppendLine($"video_shader = \"{EscapePath(shaderPath)}\"");
            }
            else
            {
                _logger.LogWarning("Shader preset not found, skipping: {Path}", shaderPath);
            }
        }

        // Bezel/overlay — Game.BezelOverridePath takes priority over the system's
        // own default (GameSystem.BezelPath), same "game overrides the broader
        // default" convention used for BIOS. RetroArch's overlay system is two
        // levels: the main config just points at a small overlay .cfg file, which
        // itself points at the actual bezel image — so a minimal overlay .cfg
        // (static full-screen image, no interactive button regions) is generated
        // here alongside the main override file.
        var bezelImagePath = !string.IsNullOrWhiteSpace(game.BezelOverridePath)
            ? game.BezelOverridePath
            : system.BezelPath;

        if (!string.IsNullOrWhiteSpace(bezelImagePath))
        {
            var resolvedBezel = ResolvePath(bezelImagePath);
            if (File.Exists(resolvedBezel))
            {
                var overlayCfgPath = Path.Combine(_outputDir, "ugl_bezel_overlay.cfg");
                var overlaySb = new System.Text.StringBuilder();
                overlaySb.AppendLine("# UGL bezel overlay — auto-generated, do not edit manually");
                overlaySb.AppendLine("overlays = 1");
                overlaySb.AppendLine($"overlay0_overlay = \"{EscapePath(resolvedBezel)}\"");
                overlaySb.AppendLine("overlay0_full_screen = true");
                overlaySb.AppendLine("overlay0_descs = 0");
                await File.WriteAllTextAsync(overlayCfgPath, overlaySb.ToString(), ct);
                _lastGeneratedOverlayCfgPath = overlayCfgPath;

                sb.AppendLine();
                sb.AppendLine("# Bezel overlay:");
                sb.AppendLine("input_overlay_enable = true");
                sb.AppendLine($"input_overlay = \"{EscapePath(overlayCfgPath)}\"");
                sb.AppendLine("input_overlay_opacity = 1.000000");
                sb.AppendLine("input_overlay_scale = 1.000000");

                _logger.LogInformation("Bezel overlay generated: {Path} (image: {Image})", overlayCfgPath, resolvedBezel);
            }
            else
            {
                _logger.LogWarning("Bezel image not found, skipping: {Path}", resolvedBezel);
            }
        }

        // Additional raw config lines
        if (!string.IsNullOrWhiteSpace(ra.AdditionalConfig))
        {
            sb.AppendLine();
            sb.AppendLine("# Additional per-emulator config:");
            foreach (var line in ra.AdditionalConfig.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                sb.AppendLine(line.Trim());
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), ct);
        _lastGeneratedPath = outputPath;

        _logger.LogInformation(
            "RetroArch override config written: {Path} (core: {Core}, run-ahead: {Ra})",
            outputPath, Path.GetFileName(corePath), ra.RunAheadFrames);

        return outputPath;
    }

    public void Cleanup()
    {
        DeleteIfExists(ref _lastGeneratedPath);
        DeleteIfExists(ref _lastGeneratedOverlayCfgPath);
    }

    private void DeleteIfExists(ref string? path)
    {
        if (path is not null && File.Exists(path))
        {
            try
            {
                File.Delete(path);
                _logger.LogDebug("Generated RetroArch config deleted: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete generated RetroArch config: {Path}", path);
            }
        }
        path = null;
    }

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

    private static string EscapePath(string path) =>
        path.Replace("\\", "\\\\");
}
