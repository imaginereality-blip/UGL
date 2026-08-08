using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using UGL.App.ViewModels;

namespace UGL.App.Services;

/// <summary>
/// Bakes the "3D Category Title Graphic" (see
/// category_card_title_overlay/3D Title Graphic Bake - Implementation Spec.md) to a
/// transparent PNG per category, using an offscreen WebView2 instance to run the real
/// three.js scene from racing-3d-title.html (templated — see TitleGraphicsBakeAssets/).
///
/// Avalonia has no 3D renderer, so this is the "headless browser" doing the actual
/// WebGL rendering; the rest of the app only ever touches the resulting PNG.
///
/// WebView2 is apartment-threaded — every call into the control must happen on the
/// thread that created it, and that thread needs a running message loop for WebView2's
/// internal async callbacks/compositing to work at all. This class owns a dedicated STA
/// thread running a genuine (if off-screen) WinForms message loop (`Application.Run`)
/// hosting the official `Microsoft.Web.WebView2.WinForms.WebView2` control — the
/// documented, tested way to host WebView2 outside WPF/UWP. An earlier version of this
/// class hand-rolled a raw Win32 message pump instead; it crashed the whole process
/// (not a catchable .NET exception) because the host window it created was never
/// actually WS_VISIBLE, which WebView2's compositor apparently doesn't tolerate even
/// when off-screen. WinForms handles all of that plumbing correctly, including
/// installing a WindowsFormsSynchronizationContext automatically so `await` inside
/// baking code just resumes back on this thread with no custom SynchronizationContext
/// needed.
/// </summary>
public sealed class TitleGraphicsBaker : IDisposable
{
    private const int CaptureWidth = 900;
    private const int CaptureHeight = 500;

    /// <summary>Raised on the baker's own thread after a category's PNG is successfully
    /// (re)written to disk — CategoryCard subscribes to swap from the live 2D fallback
    /// to the baked image without waiting for the next unrelated settings change.</summary>
    public event Action<string>? CategoryBaked;

    private readonly ILogger<TitleGraphicsBaker> _logger;
    private readonly string _cacheDir;
    private readonly string _templatePath;
    private readonly string _stageScriptPath;
    private readonly string _workHtmlPath;

    private Thread? _pumpThread;
    private Form? _form;
    private WebView2? _webView;
    private readonly TaskCompletionSource<bool> _readyTcs = new();

    // Every bake shares one WebView2 instance and one work HTML file — without this,
    // two bakes dispatched close together (e.g. a category save landing while the
    // Settings-tab preview's debounced re-bake is also in flight) can genuinely run
    // concurrently, since Form.BeginInvoke just starts each one's async chain and
    // returns immediately. One bake's Navigate() then aborts/replaces the other's
    // in-flight page load, and BOTH bakes' WebMessageReceived handlers are subscribed
    // at once — so whichever bake's "rendered" signal arrives first can satisfy the
    // OTHER bake's wait, causing it to capture and save someone else's label/colors
    // under its own category id. Serializing the whole navigate→wait→capture body
    // below is what actually fixes that, not the CancellationToken plumbing in
    // BakeCategoryAsync (which only ever cancelled the debounce *before* a bake was
    // dispatched, never a bake already in flight).
    private readonly SemaphoreSlim _bakeMutex = new(1, 1);

    public TitleGraphicsBaker(ILogger<TitleGraphicsBaker> logger)
    {
        _logger = logger;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDir = Path.Combine(localAppData, "UGL", "TitleGraphics");
        Directory.CreateDirectory(_cacheDir);

        var assetsDir = Path.Combine(AppContext.BaseDirectory, "TitleGraphicsBakeAssets");
        _templatePath = Path.Combine(assetsDir, "template.html");
        _stageScriptPath = Path.Combine(assetsDir, "three-d-stage.js");

        // The work HTML's <script src="./three-d-stage.js"> is a same-directory relative
        // path, so a copy of the script needs to live next to it. Writing both into
        // _cacheDir (not assetsDir) keeps the app's own install folder untouched — it may
        // not be writable for a Program-Files-style install even though this app is
        // normally run portable.
        _workHtmlPath = Path.Combine(_cacheDir, "_bake.html");
        File.Copy(_stageScriptPath, Path.Combine(_cacheDir, "three-d-stage.js"), overwrite: true);
    }

    private string SidecarPath(string categoryId) => Path.Combine(_cacheDir, categoryId + ".json");
    public string ImagePath(string categoryId) => Path.Combine(_cacheDir, categoryId + ".png");

    /// <summary>Cached PNG path if it exists and its sidecar hash matches the current
    /// style settings + label — null otherwise (caller should bake or show the live
    /// 2D fallback while a bake is pending).</summary>
    public string? GetCachedImageIfFresh(string categoryId, string label)
    {
        var imagePath = ImagePath(categoryId);
        var sidecarPath = SidecarPath(categoryId);
        if (!File.Exists(imagePath) || !File.Exists(sidecarPath)) return null;

        try
        {
            var sidecar = JsonSerializer.Deserialize<BakeSidecar>(File.ReadAllText(sidecarPath));
            if (sidecar is null) return null;
            if (sidecar.Label != label) return null;
            if (sidecar.StyleHash != TitleGraphicsSettings.StyleHash()) return null;
            return imagePath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Bakes (or re-bakes) a single category's title graphic. Safe to call from
    /// any thread — the actual WebView2 work is marshaled onto the baker's dedicated
    /// thread. Returns the cached PNG path on success, or null if baking failed (caller
    /// should keep showing the live 2D fallback).</summary>
    public async Task<string?> BakeCategoryAsync(string categoryId, string label, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(categoryId) || string.IsNullOrWhiteSpace(label)) return null;

        EnsurePumpStarted();
        bool ready;
        try
        {
            ready = await _readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ready = false;
        }

        // Init failed (WebView2 runtime missing, form/control creation failed, etc.) —
        // bail out here rather than risk touching a null form/control below.
        if (!ready || _form is null || !_form.IsHandleCreated) return null;

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _form.BeginInvoke((MethodInvoker)(() => _ = BakeOnUiThreadAsync(categoryId, label, tcs)));
        }
        catch (Exception ex)
        {
            // e.g. handle destroyed mid-shutdown — treat as a failed bake, not a crash.
            _logger.LogWarning(ex, "TitleGraphicsBaker: BeginInvoke failed for {CategoryId}.", categoryId);
            return null;
        }

        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    /// <summary>Re-bakes every given category — called when the global style settings
    /// (colors/rotation) change, since that invalidates every existing cached PNG.</summary>
    public async Task RebakeAllAsync(IEnumerable<(string CategoryId, string Label)> categories, CancellationToken ct = default)
    {
        foreach (var (categoryId, label) in categories)
        {
            ct.ThrowIfCancellationRequested();
            await BakeCategoryAsync(categoryId, label, ct).ConfigureAwait(false);
        }
    }

    private void EnsurePumpStarted()
    {
        if (_pumpThread is not null) return;

        _pumpThread = new Thread(PumpLoop)
        {
            IsBackground = true,
            Name = "UGL.TitleGraphicsBake.Pump",
        };
        _pumpThread.SetApartmentState(ApartmentState.STA);
        _pumpThread.Start();
    }

    /// <summary>Runs on the dedicated WinForms message-pump thread. Everything WebView2
    /// touches — the control, navigation, capture — happens inside this method's
    /// callbacks, which is what "on the UI thread" means for this class.</summary>
    private async Task BakeOnUiThreadAsync(string categoryId, string label, TaskCompletionSource<string?> tcs)
    {
        await _bakeMutex.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_webView?.CoreWebView2 is not { } webView)
            {
                tcs.TrySetResult(null);
                return;
            }

            var html = BuildHtml(label);
            File.WriteAllText(_workHtmlPath, html, Encoding.UTF8);

            var signalTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnMessage(object? s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                var msg = e.TryGetWebMessageAsString();
                if (msg is not null) signalTcs.TrySetResult(msg);
            }

            webView.WebMessageReceived += OnMessage;
            try
            {
                // Cache-bust with a query string — WebView2/Edge can otherwise serve a
                // cached response for the same file:// URL instead of re-reading
                // _workHtmlPath's just-written content.
                webView.Navigate(new Uri(_workHtmlPath).AbsoluteUri + "?b=" + Guid.NewGuid().ToString("N"));
                var completed = await signalTcs.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);

                if (completed.StartsWith("error:", StringComparison.Ordinal))
                {
                    _logger.LogWarning("Title graphic bake failed for {CategoryId}: {Error}", categoryId, completed);
                    tcs.TrySetResult(null);
                    return;
                }

                var tempPng = ImagePath(categoryId) + ".tmp";
                await using (var stream = File.Create(tempPng))
                {
                    await webView.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream)
                        .ConfigureAwait(true);
                }

                var finalPng = ImagePath(categoryId);
                File.Copy(tempPng, finalPng, overwrite: true);
                File.Delete(tempPng);

                var sidecar = new BakeSidecar { Label = label, StyleHash = TitleGraphicsSettings.StyleHash() };
                File.WriteAllText(SidecarPath(categoryId), JsonSerializer.Serialize(sidecar));

                _logger.LogInformation("Baked title graphic for category {CategoryId}.", categoryId);
                tcs.TrySetResult(finalPng);
                CategoryBaked?.Invoke(categoryId);
            }
            finally
            {
                webView.WebMessageReceived -= OnMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Title graphic bake threw for {CategoryId}.", categoryId);
            tcs.TrySetResult(null);
        }
        finally
        {
            _bakeMutex.Release();
        }
    }

    private string BuildHtml(string label)
    {
        var template = File.ReadAllText(_templatePath);
        var s = TitleGraphicsSettings.StyleSnapshotForBake();

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return template
            .Replace("__TITLE_JSON__", JsonSerializer.Serialize(label))
            .Replace("__FILL_TOP__", NormalizeHexForThree(s.FillTopColor))
            .Replace("__FILL_MID__", NormalizeHexForThree(s.FillMidColor))
            .Replace("__FILL_BOTTOM__", NormalizeHexForThree(s.FillBottomColor))
            .Replace("__BEVEL_COLOR__", NormalizeHexForThree(s.BevelColor))
            .Replace("__OUTLINE_COLOR__", NormalizeHexForThree(s.OutlineColor))
            .Replace("__LIGHT_COUNT__", Math.Clamp(s.LightCount, 1, 3).ToString(ci))
            .Replace("__LIGHT_MAIN_COLOR__", NormalizeHexForThree(s.LightMainColor))
            .Replace("__LIGHT_KEY1_COLOR__", NormalizeHexForThree(s.LightKey1Color))
            .Replace("__LIGHT_KEY2_COLOR__", NormalizeHexForThree(s.LightKey2Color))
            .Replace("__ROTATION_X__", s.RotationXDegrees.ToString(ci))
            .Replace("__ROTATION_Y__", s.RotationYDegrees.ToString(ci))
            .Replace("__ROTATION_Z__", s.RotationZDegrees.ToString(ci));
    }

    /// <summary>
    /// three.js's Color parser only accepts 3- or 6-digit "#rgb"/"#rrggbb" — an
    /// 8-digit "#AARRGGBB" (which TitleGraphicsConfigViewModel's picker used to emit
    /// before that was fixed) silently fails to parse and falls back to white. This
    /// is a defensive normalization at the baking boundary, not just a fix at the
    /// picker: any value already persisted to settings.json from before that fix
    /// stays 8-digit forever unless re-picked by hand, and BuildHtml is the one place
    /// every color — regardless of source — passes through before reaching the JS
    /// template, so it's the right place to guarantee a valid 6-digit value reaches
    /// three.js no matter what's on disk.
    /// </summary>
    private static string NormalizeHexForThree(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "#FFFFFF";
        var h = hex.Trim();
        if (h.Length == 9 && h[0] == '#') return "#" + h[3..]; // "#AARRGGBB" -> "#RRGGBB"
        if (h.Length == 7 && h[0] == '#') return h;             // already "#RRGGBB"
        if (h.Length == 4 && h[0] == '#') return h;             // already "#RGB"
        return "#FFFFFF";
    }

    // ── WinForms message-pump thread ─────────────────────────────────────

    private void PumpLoop()
    {
        try
        {
            // A real, WS_VISIBLE top-level window positioned far off any monitor —
            // invisible to the user, but genuinely "visible" as far as Win32/DWM are
            // concerned, which WebView2's compositor requires to present frames at all.
            _form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                Size = new Size(CaptureWidth, CaptureHeight),
                ShowInTaskbar = false,
            };

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.Transparent,
            };
            _form.Controls.Add(_webView);

            _form.Load += async (_, _) => await InitializeWebViewAsync().ConfigureAwait(true);
            _form.Show();

            Application.Run(_form);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TitleGraphicsBaker: pump thread failed to start.");
            _readyTcs.TrySetResult(false);
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(_cacheDir, "WebView2UserData");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder).ConfigureAwait(true);
            await _webView!.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            _readyTcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TitleGraphicsBaker: WebView2 initialization failed (WebView2 runtime missing?). " +
                "Title graphics will fall back to the live 2D overlay.");
            _readyTcs.TrySetResult(false);
        }
    }

    public void Dispose()
    {
        if (_pumpThread is null || _form is null) return;

        try
        {
            if (_form.IsHandleCreated)
                _form.BeginInvoke((MethodInvoker)(() =>
                {
                    _webView?.Dispose();
                    _form.Dispose();
                    Application.ExitThread();
                }));
        }
        catch
        {
            // Thread may already be gone — nothing more to clean up.
        }

        _pumpThread.Join(3000);
    }

    private sealed class BakeSidecar
    {
        public string Label { get; set; } = string.Empty;
        public string StyleHash { get; set; } = string.Empty;
    }
}
