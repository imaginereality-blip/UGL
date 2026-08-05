using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace UGL.App.Views.Controls;

/// <summary>
/// The comic-book title-text treatment from category_card_title_overlay/README.md
/// (direction 28b) — gradient fill, black+purple double outline, straight-down
/// cyan/magenta extrusion, -5° italic tilt. A single reusable control so
/// CategoryCard and the Title Graphics settings preview render byte-for-byte the
/// same visual instead of two independently-maintained copies of ~21 TextBlocks.
/// Renders whatever TitleText is given as-is — callers decide casing (CategoryCard
/// binds it through UppercaseConverter; the settings preview passes an already
/// upper-cased literal).
/// </summary>
public sealed partial class CategoryTitleGraphic : UserControl
{
    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<CategoryTitleGraphic, string>(
            nameof(TitleText), defaultValue: string.Empty, defaultBindingMode: BindingMode.OneWay);

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public CategoryTitleGraphic()
    {
        InitializeComponent();
    }
}
