using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Updates;

/// <summary>
/// Checks a GitHub repo's Releases for a newer version and applies a portable-ZIP
/// update in place.
///
/// IMPORTANT — set the actual repo before shipping: RepoOwner/RepoName below are
/// placeholders. Also set &lt;Version&gt;X.Y.Z&lt;/Version&gt; in UGL.App.csproj — CurrentVersion
/// reads whatever version is embedded in the compiled exe, defaulting to "0.0.0" if
/// none was set, which would make every release look newer than "no version at all".
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string RepoOwner = "imaginereality-blip";
    private const string RepoName = "UGL";

    private readonly HttpClient _http;
    private readonly ILogger<GitHubUpdateService> _logger;

    public event Action<UpdateCheckResult>? UpdateAvailable;

    public string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    public GitHubUpdateService(ILogger<GitHubUpdateService> logger)
    {
        _logger = logger;
        _http = new HttpClient();
        // GitHub's API rejects requests with no User-Agent header (returns 403).
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("UGL-UpdateChecker/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public void CheckForUpdateInBackground()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await CheckForUpdateAsync();
                if (result.IsUpdateAvailable)
                    UpdateAvailable?.Invoke(result);
            }
            catch (Exception ex)
            {
                // Never let a background check surface as an unhandled exception —
                // a failed startup check should be invisible, not disruptive.
                _logger.LogDebug(ex, "Background update check failed (non-fatal).");
            }
        });
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var current = CurrentVersion;
        var noUpdate = new UpdateCheckResult { IsUpdateAvailable = false, CurrentVersion = current };

        if (RepoOwner == "YOUR_GITHUB_USERNAME")
        {
            _logger.LogWarning("Update check skipped — GitHubUpdateService.RepoOwner/RepoName are still placeholders.");
            return noUpdate;
        }

        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var release = await _http.GetFromJsonAsync<GitHubRelease>(url, ct);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                _logger.LogInformation("Update check: no releases found for {Owner}/{Repo}.", RepoOwner, RepoName);
                return noUpdate;
            }

            var latestTag = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(latestTag, out var latestVersion) ||
                !Version.TryParse(current, out var currentVersion))
            {
                _logger.LogWarning("Update check: couldn't parse version (current='{Current}', latest='{Latest}').", current, release.TagName);
                return noUpdate;
            }

            if (latestVersion <= currentVersion)
            {
                _logger.LogInformation("Update check: already up to date ({Current}).", current);
                return noUpdate;
            }

            var zipAsset = release.Assets?.FirstOrDefault(a =>
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (zipAsset is null)
            {
                _logger.LogWarning("Update check: release {Tag} has no .zip asset — nothing to download.", release.TagName);
                return noUpdate;
            }

            _logger.LogInformation("Update available: {Current} -> {Latest}", current, latestTag);

            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                CurrentVersion = current,
                LatestVersion = latestTag,
                ReleaseNotesUrl = release.HtmlUrl,
                ReleaseNotes = release.Body ?? string.Empty,
                DownloadUrl = zipAsset.BrowserDownloadUrl,
                DownloadSizeBytes = zipAsset.Size,
            };
        }
        catch (Exception ex)
        {
            // No network, DNS failure, GitHub rate limit, malformed response, etc. —
            // all treated the same way: log it, report "no update", never throw.
            _logger.LogDebug(ex, "Update check failed (non-fatal).");
            return noUpdate;
        }
    }

    public async Task ApplyUpdateAsync(UpdateCheckResult update, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!update.IsUpdateAvailable || string.IsNullOrWhiteSpace(update.DownloadUrl))
            throw new InvalidOperationException("No update to apply.");

        var workDir = Path.Combine(Path.GetTempPath(), $"UGL_Update_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var zipPath = Path.Combine(workDir, "update.zip");
        var extractDir = Path.Combine(workDir, "extracted");

        try
        {
            progress?.Report("Downloading update…");
            await using (var stream = await _http.GetStreamAsync(update.DownloadUrl, ct))
            await using (var file = File.Create(zipPath))
                await stream.CopyToAsync(file, ct);

            progress?.Report("Extracting update…");
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            // If the zip contains a single root folder (common when GitHub/zip tools
            // wrap the release in a folder named after the release), step into it so
            // we're copying the actual app files, not one extra nesting level.
            var entries = Directory.GetFileSystemEntries(extractDir);
            var sourceDir = entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : extractDir;

            progress?.Report("Applying update…");
            var scriptPath = WriteUpdateScript(sourceDir, AppContext.BaseDirectory, workDir);

            progress?.Report("Restarting…");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });

            // The helper script waits for this process to exit before touching any
            // files, so the app must actually close now rather than just returning —
            // otherwise the exe (and any DLL currently loaded) is still locked.
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply update.");
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
            throw;
        }
    }

    /// <summary>
    /// Writes a small batch script that waits for this process to exit, copies the
    /// extracted update over the install directory (skipping every folder the app
    /// treats as user data — must be kept in sync with AppFolderScaffolder.Folders in
    /// UGL.App, since that's the canonical list of what belongs to the user, not the
    /// app), relaunches UGL, then deletes the temp working folder and itself.
    /// </summary>
    private static string WriteUpdateScript(string sourceDir, string installDir, string workDir)
    {
        // Kept in sync with AppFolderScaffolder.Folders — top-level folder names only.
        string[] preserveFolders =
        [
            "config", "media", "roms", "emulators", "bios", "bezels", "addons", "retroarch", "logs",
        ];

        var exePath = Path.Combine(installDir, "UGL.exe");
        var scriptPath = Path.Combine(workDir, "apply_update.bat");
        var pid = Environment.ProcessId;

        var xcopyExcludeFile = Path.Combine(workDir, "exclude.txt");
        // Explicit ASCII, not the default UTF-8 — xcopy is a legacy tool that can
        // fail to read its exclude file correctly under UTF-8, and every one of
        // these folder names is plain ASCII anyway, so there's no reason to risk it.
        File.WriteAllLines(xcopyExcludeFile, preserveFolders.Select(f => $"\\{f}\\"), System.Text.Encoding.ASCII);

        var script = $"""
            @echo off
            :: Wait for UGL to actually exit before touching any files it might still have open.
            :wait
            tasklist /FI "PID eq {pid}" 2>NUL | find "{pid}" >NUL
            if not errorlevel 1 (
                timeout /t 1 /nobreak >NUL
                goto wait
            )

            :: Copy the update over the install directory, excluding anything the app
            :: treats as user data (see exclude.txt, written alongside this script).
            xcopy "{sourceDir}\*" "{installDir}\" /E /Y /EXCLUDE:{xcopyExcludeFile}

            :: Relaunch and clean up.
            start "" "{exePath}"
            timeout /t 2 /nobreak >NUL
            rmdir /S /Q "{workDir}"
            """;

        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    // ── GitHub API response shapes ──────────────────────────────────────────

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = string.Empty;
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = string.Empty;
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
