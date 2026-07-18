namespace UGL.App.ViewModels;

/// <summary>
/// Live snapshot of the card-highlight appearance settings (color, intensity, style).
///
/// GameCard and CategoryCard are instantiated by Avalonia's DataTemplate system, not
/// the DI container, so they can't constructor-inject IConfigurationService the way
/// DI-managed ViewModels do. This static bridge lets them read current settings anyway,
/// and react immediately when CardHighlightConfigViewModel changes one — e.g. so a
/// visible, selected card updates live while you're adjusting a slider in Settings,
/// not just the next time it happens to reselect.
///
/// Loaded once at startup from AppSettings by MainWindowViewModel; kept in sync after
/// that by CardHighlightConfigViewModel on every change.
/// </summary>
public static class CardHighlightSettings
{
    public static string Color     { get; set; } = "#FFFFD700";
    public static double Intensity { get; set; } = 1.0;
    public static string Style     { get; set; } = "Solid"; // "Solid" | "Pulsing"
    public static int    Thickness { get; set; } = 4; // pixels, 2-5

    public static event Action? Changed;

    public static void Load(string color, double intensity, string style, int thickness)
    {
        Color = color;
        Intensity = intensity;
        Style = style;
        Thickness = thickness;
        Changed?.Invoke();
    }
}
