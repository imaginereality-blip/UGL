using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UGL.App.Views;

/// <summary>Converts a "#RRGGBB" hex string (as typed in Title Graphics color fields)
/// into a SolidColorBrush for a small swatch preview. Falls back to gray on parse
/// failure — a half-typed hex value while editing shouldn't crash the binding.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            try { return new SolidColorBrush(Color.Parse(s)); }
            catch { /* fall through to gray below while the user is mid-edit */ }
        }
        return new SolidColorBrush(Color.Parse("#666666"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
