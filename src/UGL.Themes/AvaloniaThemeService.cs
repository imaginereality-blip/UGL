using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Themes;

/// <summary>
/// Production IThemeService. Loads themes from themes.json and applies the
/// active theme to Avalonia's Application.Resources at runtime by merging
/// a ResourceDictionary containing all UGL.* resource keys.
///
/// All AXAML files that use {DynamicResource UGL.XxxKey} will update
/// automatically when ApplyThemeAsync is called — no restart required.
/// </summary>
public sealed class AvaloniaThemeService : IThemeService
{
    private readonly ILogger<AvaloniaThemeService> _logger;
    private readonly string _themesJsonPath;
    private List<Theme> _themes = [];

    public Theme ActiveTheme { get; private set; } =
        new Theme { Id = "default", Name = "Default" };

    public event EventHandler<Theme>? ThemeChanged;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public AvaloniaThemeService(ILogger<AvaloniaThemeService> logger)
    {
        _logger = logger;
        _themesJsonPath = Path.Combine(AppContext.BaseDirectory, "config", "themes.json");
    }

    public async Task<IReadOnlyList<Theme>> GetAvailableThemesAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _themes.AsReadOnly();
    }

    public async Task ApplyThemeAsync(string themeId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        var theme = _themes.FirstOrDefault(
            t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase))
            ?? _themes.FirstOrDefault()
            ?? ActiveTheme;

        ActiveTheme = theme;

        // Must run on UI thread — Avalonia resource dictionaries are not thread-safe
        await Dispatcher.UIThread.InvokeAsync(() => ApplyToResources(theme));

        ThemeChanged?.Invoke(this, theme);
        _logger.LogInformation("Theme applied: {Id} ({Name})", theme.Id, theme.Name);
    }

    // ── Resource application ───────────────────────────────────────────────

    private static void ApplyToResources(Theme theme)
    {
        var app = Application.Current;
        if (app is null) return;

        var dict = BuildResourceDictionary(theme);

        // Remove any previously applied UGL theme dictionary and insert the new one.
        var merged = app.Resources.MergedDictionaries;
        var existing = merged.OfType<Avalonia.Controls.ResourceDictionary>()
                             .FirstOrDefault(d => d.ContainsKey("__UGL_THEME__"));
        if (existing is not null)
            merged.Remove(existing);

        merged.Add(dict);
    }

    private static Avalonia.Controls.ResourceDictionary BuildResourceDictionary(Theme theme)
    {
        var accent    = ParseColor(theme.AccentColor,       Colors.DodgerBlue);
        var bg        = ParseColor(theme.BackgroundColor,   Color.Parse("#FF1A1A2E"));
        var surface   = ParseColor(theme.SurfaceColor,      Color.Parse("#FF16213E"));
        var textPri   = ParseColor(theme.TextPrimaryColor,  Colors.White);
        var textSec   = ParseColor(theme.TextSecondaryColor, Color.Parse("#FFB0B0C0"));
        var selection = ParseColor(theme.SelectionColor,    Color.Parse("#FF0F3460"));

        // Derived colours
        var hintBarBg   = Color.FromArgb(0xCC, bg.R, bg.G, bg.B);
        var overlayBg   = Color.FromArgb(0xEE, (byte)(bg.R / 2), (byte)(bg.G / 2), (byte)(bg.B / 2));
        var borderDef   = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        var borderSel   = accent;
        var cardGradTop = selection;
        var cardGradBot = bg;

        // Shared chrome tokens (recommendation 1) — one named gray/scrim/panel instead
        // of the ~8 near-identical hardcoded literals previously hand-rolled per view.
        var divider        = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
        var scrimHeavy      = Color.FromArgb(0xF0, (byte)(bg.R / 2), (byte)(bg.G / 2), (byte)(bg.B / 2));
        var panelBackground = Color.FromArgb(0x11, (byte)(bg.R / 2), (byte)(bg.G / 2), (byte)(bg.B / 2));
        var sidebarSelectionTint = Color.FromArgb(0x29, accent.R, accent.G, accent.B);

        // Status / destructive colours — Danger matches the quitRow red already in use.
        var success = Color.Parse("#FF44FF88");
        var danger  = Color.Parse("#FFFF5C5C");

        return new Avalonia.Controls.ResourceDictionary
        {
            // Sentinel key so we can find and replace this dictionary later
            ["__UGL_THEME__"] = theme.Id,

            // ── Colour brushes ──────────────────────────────────────────────
            [ThemeKeys.Accent]              = new SolidColorBrush(accent),
            [ThemeKeys.Background]          = new SolidColorBrush(bg),
            [ThemeKeys.Surface]             = new SolidColorBrush(surface),
            [ThemeKeys.TextPrimary]         = new SolidColorBrush(textPri),
            [ThemeKeys.TextSecondary]       = new SolidColorBrush(textSec),
            [ThemeKeys.Selection]           = new SolidColorBrush(selection),
            [ThemeKeys.HintBarBg]           = new SolidColorBrush(hintBarBg),
            [ThemeKeys.OverlayBg]           = new SolidColorBrush(overlayBg),
            [ThemeKeys.CardBorderDefault]   = new SolidColorBrush(borderDef),
            [ThemeKeys.CardBorderSelected]  = new SolidColorBrush(borderSel),
            [ThemeKeys.CardGradientTop]     = new SolidColorBrush(cardGradTop),
            [ThemeKeys.CardGradientBot]     = new SolidColorBrush(cardGradBot),
            [ThemeKeys.Divider]             = new SolidColorBrush(divider),
            [ThemeKeys.ScrimHeavy]          = new SolidColorBrush(scrimHeavy),
            [ThemeKeys.PanelBackground]     = new SolidColorBrush(panelBackground),
            [ThemeKeys.SidebarSelectionTint] = new SolidColorBrush(sidebarSelectionTint),
            [ThemeKeys.Success]             = new SolidColorBrush(success),
            [ThemeKeys.Danger]              = new SolidColorBrush(danger),

            // ── Typography ──────────────────────────────────────────────────
            [ThemeKeys.FontFamily]          = theme.FontFamily,
            [ThemeKeys.TitleFontSize]       = theme.TitleFontSize,
            [ThemeKeys.BodyFontSize]        = theme.BodyFontSize,

            // ── Layout ──────────────────────────────────────────────────────
            [ThemeKeys.CardCornerRadius]    = new CornerRadius(theme.CardCornerRadius),
            [ThemeKeys.CardSpacing]         = new Thickness(theme.CardSpacing / 2),
            [ThemeKeys.SelectionScaleFactor] = theme.SelectionScaleFactor,
            [ThemeKeys.AnimationDurationMs] = theme.AnimationDurationMs,
        };
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return Color.Parse(hex); }
        catch { return fallback; }
    }

    // ── JSON loading ───────────────────────────────────────────────────────

    private bool _loaded;
    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        _loaded = true;

        if (!File.Exists(_themesJsonPath))
        {
            _logger.LogWarning("themes.json not found at {Path}. Using built-in default.", _themesJsonPath);
            _themes = [BuiltInDefault()];
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_themesJsonPath);
            _themes = await JsonSerializer.DeserializeAsync<List<Theme>>(stream, JsonOptions, ct) ?? [];
            _logger.LogInformation("Loaded {Count} themes from themes.json.", _themes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load themes.json. Using built-in default.");
            _themes = [BuiltInDefault()];
        }

        // Always ensure a default exists
        if (_themes.Count == 0)
            _themes.Add(BuiltInDefault());
    }

    private static Theme BuiltInDefault() => new()
    {
        Id = "default", Name = "Default Dark",
        AccentColor = "#FF0078D4", BackgroundColor = "#FF1A1A2E",
        SurfaceColor = "#FF16213E", TextPrimaryColor = "#FFFFFFFF",
        TextSecondaryColor = "#FFB0B0C0", SelectionColor = "#FF0F3460",
        FontFamily = "Segoe UI", TitleFontSize = 28, BodyFontSize = 16,
        CardCornerRadius = 0, CardSpacing = 4,
        SelectionScaleFactor = 1.12, AnimationDurationMs = 150,
    };
}
