namespace UGL.Themes;

/// <summary>
/// Central source of truth for all DynamicResource keys used by the UGL theme engine.
/// AXAML files bind to these keys via {DynamicResource UGL.XxxKey}.
/// AvaloniaThemeService populates them from the active Theme model.
/// </summary>
public static class ThemeKeys
{
    // ── Colours ────────────────────────────────────────────────────────────
    public const string Accent          = "UGL.AccentColor";
    public const string Background      = "UGL.BackgroundColor";
    public const string Surface         = "UGL.SurfaceColor";
    public const string TextPrimary     = "UGL.TextPrimaryColor";
    public const string TextSecondary   = "UGL.TextSecondaryColor";
    public const string Selection       = "UGL.SelectionColor";

    // Derived / composite colours (computed from base colours)
    public const string HintBarBg       = "UGL.HintBarBackground";
    public const string OverlayBg       = "UGL.OverlayBackground";
    public const string CardBorderDefault = "UGL.CardBorderDefault";
    public const string CardBorderSelected = "UGL.CardBorderSelected";
    public const string CardGradientTop  = "UGL.CardGradientTop";
    public const string CardGradientBot  = "UGL.CardGradientBottom";

    // ── Typography ─────────────────────────────────────────────────────────
    public const string FontFamily      = "UGL.FontFamily";
    public const string TitleFontSize   = "UGL.TitleFontSize";
    public const string BodyFontSize    = "UGL.BodyFontSize";

    // ── Layout ─────────────────────────────────────────────────────────────
    public const string CardCornerRadius      = "UGL.CardCornerRadius";
    public const string CardSpacing           = "UGL.CardSpacing";
    public const string SelectionScaleFactor  = "UGL.SelectionScaleFactor";
    public const string AnimationDurationMs   = "UGL.AnimationDurationMs";
}
