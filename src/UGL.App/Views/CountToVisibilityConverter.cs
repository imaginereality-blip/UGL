using System.Globalization;
using Avalonia.Data.Converters;

namespace UGL.App.Views;

/// <summary>
/// Converts a collection Count integer to a bool for empty-state visibility.
/// Usage in XAML: Converter="{x:Static views:CountToVisibilityConverter.IsZero}"
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    /// <summary>Returns true (visible) when Count == 0.</summary>
    public static readonly CountToVisibilityConverter IsZero = new(showWhenZero: true);

    /// <summary>Returns true (visible) when Count > 0.</summary>
    public static readonly CountToVisibilityConverter IsNonZero = new(showWhenZero: false);

    private readonly bool _showWhenZero;

    private CountToVisibilityConverter(bool showWhenZero)
        => _showWhenZero = showWhenZero;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        return _showWhenZero ? count == 0 : count > 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
