using System.Globalization;
using Avalonia.Data.Converters;

namespace UGL.App.Views;

/// <summary>
/// Upper-cases the bound string. Used by the category-card title graphic
/// (Settings → Title Graphics), which always renders ALL CAPS regardless of
/// how the category's own Label is cased.
/// </summary>
public sealed class UppercaseConverter : IValueConverter
{
    public static readonly UppercaseConverter Instance = new();

    private UppercaseConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? s.ToUpperInvariant() : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
