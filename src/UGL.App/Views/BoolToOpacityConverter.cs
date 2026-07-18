using System.Globalization;
using Avalonia.Data.Converters;

namespace UGL.App.Views;

/// <summary>
/// Returns 1.0 when the bound bool is true (selected), 0.4 when false.
/// Used in the category tab bar to dim unselected labels without
/// requiring a full style trigger.
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    private BoolToOpacityConverter() { }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.4;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
