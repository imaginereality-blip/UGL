using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace UGL.App.Views;

/// <summary>
/// Collapses the Settings sidebar column to 0 once content is entered (A-enter),
/// giving the active tab the full width; back to 240px once focus returns to the
/// sidebar. See ConfigEditorViewModel.IsContentFocused / EnterContent / ExitContent.
/// </summary>
public sealed class BoolToSidebarWidthConverter : IValueConverter
{
    public static readonly BoolToSidebarWidthConverter Instance = new();

    private BoolToSidebarWidthConverter() { }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new GridLength(0) : new GridLength(240);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
