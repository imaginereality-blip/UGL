namespace UGL.App.ViewModels;

/// <summary>
/// Live snapshot of the title-graphics overlay settings (on/off, placement).
///
/// CategoryCard is instantiated by Avalonia's DataTemplate system, not the DI
/// container, so it can't constructor-inject IConfigurationService the way
/// DI-managed ViewModels do. This static bridge lets it read current settings
/// anyway, and react immediately when TitleGraphicsConfigViewModel changes one —
/// same pattern as CardHighlightSettings.
///
/// Loaded once at startup from AppSettings by MainWindowViewModel; kept in sync
/// after that by TitleGraphicsConfigViewModel on every change.
/// </summary>
public static class TitleGraphicsSettings
{
    public static bool   Enabled   { get; private set; }
    public static string Placement { get; private set; } = "Middle"; // "Top" | "Middle" | "Bottom"

    public static event Action? Changed;

    public static void Load(bool enabled, string placement)
    {
        Enabled = enabled;
        Placement = placement;
        Changed?.Invoke();
    }
}
