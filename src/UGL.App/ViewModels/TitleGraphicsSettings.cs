namespace UGL.App.ViewModels;

/// <summary>
/// Live snapshot of the title-graphics overlay settings (on/off, scale/position, and
/// the 3D bake style used by TitleGraphicsBaker).
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
    public static bool Enabled { get; private set; }

    /// <summary>Applied as a post-fit ScaleTransform/TranslateTransform on the card's
    /// title-graphic cell — see CategoryCard.axaml.cs ApplyTitleGraphic. PositionX/Y
    /// are percentages of the card's own width/height, not pixels, so they scale
    /// correctly across different card sizes.</summary>
    public static double Scale     { get; private set; } = 0.9;
    public static double PositionX { get; private set; } = 0.0;
    public static double PositionY { get; private set; } = 0.0;

    public static string FillTopColor    { get; private set; } = "#B8860B";
    public static string FillMidColor    { get; private set; } = "#E8C848";
    public static string FillBottomColor { get; private set; } = "#FFF3C4";
    public static string BevelColor      { get; private set; } = "#A855FF";
    public static string OutlineColor    { get; private set; } = "#C41E1E";

    /// <summary>1-3. Main is always on; Key1/Key2 switch on as this increases.</summary>
    public static int    LightCount     { get; private set; } = 2;
    public static string LightMainColor { get; private set; } = "#F5C518";
    public static string LightKey1Color { get; private set; } = "#3D1466";
    public static string LightKey2Color { get; private set; } = "#FFFFFF";

    public static double RotationXDegrees { get; private set; } = 0.0;
    public static double RotationYDegrees { get; private set; } = 0.0;
    public static double RotationZDegrees { get; private set; } = 0.0;

    public static event Action? Changed;

    public static void Load(bool enabled, double scale, double positionX, double positionY)
    {
        Enabled = enabled;
        Scale = scale;
        PositionX = positionX;
        PositionY = positionY;
        Changed?.Invoke();
    }

    public static void LoadStyle(
        string fillTop, string fillMid, string fillBottom,
        string bevel, string outline,
        int lightCount, string lightMain, string lightKey1, string lightKey2,
        double rotationX, double rotationY, double rotationZ)
    {
        FillTopColor = fillTop;
        FillMidColor = fillMid;
        FillBottomColor = fillBottom;
        BevelColor = bevel;
        OutlineColor = outline;
        LightCount = lightCount;
        LightMainColor = lightMain;
        LightKey1Color = lightKey1;
        LightKey2Color = lightKey2;
        RotationXDegrees = rotationX;
        RotationYDegrees = rotationY;
        RotationZDegrees = rotationZ;
        Changed?.Invoke();
    }

    /// <summary>Hash of every style field, used by TitleGraphicsBaker to detect when a
    /// category's cached bake is stale relative to current settings. Scale/position
    /// aren't included — those are a display-time transform applied identically to
    /// both the baked PNG and the live 2D fallback, not something baked into the PNG
    /// itself, so changing them doesn't invalidate the cache.</summary>
    public static string StyleHash() => string.Join('|',
        FillTopColor, FillMidColor, FillBottomColor,
        BevelColor, OutlineColor,
        LightCount, LightMainColor, LightKey1Color, LightKey2Color,
        RotationXDegrees.ToString("F1"), RotationYDegrees.ToString("F1"), RotationZDegrees.ToString("F1"));

    /// <summary>Immutable snapshot of the current style fields, read by TitleGraphicsBaker
    /// off the UI/pump thread — avoids reading the live static properties mid-change.</summary>
    public static StyleSnapshot StyleSnapshotForBake() => new(
        FillTopColor, FillMidColor, FillBottomColor,
        BevelColor, OutlineColor,
        LightCount, LightMainColor, LightKey1Color, LightKey2Color,
        RotationXDegrees, RotationYDegrees, RotationZDegrees);

    public readonly record struct StyleSnapshot(
        string FillTopColor, string FillMidColor, string FillBottomColor,
        string BevelColor, string OutlineColor,
        int LightCount, string LightMainColor, string LightKey1Color, string LightKey2Color,
        double RotationXDegrees, double RotationYDegrees, double RotationZDegrees);
}
