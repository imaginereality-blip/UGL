using Avalonia;

namespace UGL.App.ViewModels;

/// <summary>
/// Live snapshot of the actual on-screen card size, in real device pixels (accounting
/// for display scaling), reported by GameCard/CategoryCard whenever their rendered size
/// changes. Same reasoning as CardHighlightSettings: these cards are instantiated by
/// Avalonia's DataTemplate system, not the DI container, so a static bridge is how
/// Settings reads this back out rather than a service reference.
///
/// Used to show a "recommended art size" hint in Settings — since the actual best
/// resolution for card art depends on the current window/screen size, not a fixed
/// constant, this has to be measured at runtime rather than hard-coded.
/// </summary>
public static class CardDimensionInfo
{
    public static Size GameCardPixelSize { get; private set; }
    public static Size CategoryCardPixelSize { get; private set; }

    public static event Action? Changed;

    public static void ReportGameCardSize(Size pixelSize)
    {
        if (GameCardPixelSize == pixelSize) return;
        GameCardPixelSize = pixelSize;
        Changed?.Invoke();
    }

    public static void ReportCategoryCardSize(Size pixelSize)
    {
        if (CategoryCardPixelSize == pixelSize) return;
        CategoryCardPixelSize = pixelSize;
        Changed?.Invoke();
    }
}
