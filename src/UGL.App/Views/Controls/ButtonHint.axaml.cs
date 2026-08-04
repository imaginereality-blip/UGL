using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace UGL.App.Views.Controls;

/// <summary>
/// One chip in a controller hint bar — a round glyph badge plus a label, e.g. "A  Launch".
/// Replaces the old single run-on TextBlock hint strings (HomeMenuView, GameBrowserView,
/// ConfigEditorView, FilterOverlayView) with discrete, scannable pieces.
/// </summary>
public sealed partial class ButtonHint : UserControl
{
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<ButtonHint, string>(
            nameof(Glyph), defaultValue: string.Empty, defaultBindingMode: BindingMode.OneWay);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ButtonHint, string>(
            nameof(Label), defaultValue: string.Empty, defaultBindingMode: BindingMode.OneWay);

    /// <summary>True for the primary/most-common action in a given bar (usually "A") —
    /// rendered with the accent-color badge instead of the neutral one, matching the
    /// design reference's "A Launch" chip.</summary>
    public static readonly StyledProperty<bool> IsPrimaryProperty =
        AvaloniaProperty.Register<ButtonHint, bool>(nameof(IsPrimary), defaultValue: false);

    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsPrimary
    {
        get => GetValue(IsPrimaryProperty);
        set => SetValue(IsPrimaryProperty, value);
    }

    public ButtonHint()
    {
        InitializeComponent();
    }
}
