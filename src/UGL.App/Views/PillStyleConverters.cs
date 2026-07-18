using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UGL.App.Views;

/// <summary>
/// Converts IsFocused + IsSelected pill state to a BorderBrush color.
/// Parameter "focused" checks IsFocused; anything else checks IsSelected.
/// Used in FilterOverlayView pill DataTemplate in place of WPF DataTriggers.
/// </summary>
public sealed class PillBorderBrushConverter : IValueConverter
{
    public static readonly PillBorderBrushConverter Instance = new();

    private static readonly IBrush Focused  = new SolidColorBrush(Color.Parse("#FF0078D4"));
    private static readonly IBrush Selected = new SolidColorBrush(Color.Parse("#880078D4"));
    private static readonly IBrush Default  = new SolidColorBrush(Color.Parse("#33FFFFFF"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        return parameter?.ToString() == "focused"
            ? (flag ? Focused : Default)
            : (flag ? Selected : Default);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts IsFocused + IsSelected pill state to a Background brush.
/// </summary>
public sealed class PillBackgroundConverter : IValueConverter
{
    public static readonly PillBackgroundConverter Instance = new();

    private static readonly IBrush Focused  = new SolidColorBrush(Color.Parse("#220078D4"));
    private static readonly IBrush Selected = new SolidColorBrush(Color.Parse("#1100AAFF"));
    private static readonly IBrush Default  = new SolidColorBrush(Color.Parse("#22FFFFFF"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        return parameter?.ToString() == "focused"
            ? (flag ? Focused : Default)
            : (flag ? Selected : Default);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts IsSelected to a text foreground brush (white when selected, dim otherwise).
/// </summary>
public sealed class PillTextBrushConverter : IValueConverter
{
    public static readonly PillTextBrushConverter Instance = new();

    private static readonly IBrush Selected = new SolidColorBrush(Color.Parse("#FFFFFFFF"));
    private static readonly IBrush Default  = new SolidColorBrush(Color.Parse("#CCFFFFFF"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Selected : Default;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
