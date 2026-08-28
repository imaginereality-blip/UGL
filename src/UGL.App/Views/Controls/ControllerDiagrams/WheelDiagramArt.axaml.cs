using Avalonia;
using Avalonia.Controls;

namespace UGL.App.Views.Controls.ControllerDiagrams;

/// <summary>Vector reference art for a Logitech-style racing wheel — see the .axaml
/// for layout reasoning. Named shapes correspond to ControllerControlDefinitions.
/// Wheel's ControlIds (shifter-set controls have no diagram hotspot — see the .axaml
/// comment).</summary>
public sealed partial class WheelDiagramArt : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<string>> HighlightedControlIdsProperty =
        AvaloniaProperty.Register<WheelDiagramArt, IReadOnlyList<string>>(
            nameof(HighlightedControlIds), defaultValue: []);

    public IReadOnlyList<string> HighlightedControlIds
    {
        get => GetValue(HighlightedControlIdsProperty);
        set => SetValue(HighlightedControlIdsProperty, value);
    }

    private readonly Dictionary<string, Control> _shapesByControlId = new();

    public WheelDiagramArt()
    {
        InitializeComponent();
        RegisterShape("LeftPaddle",  "ShapeLeftPaddle");
        RegisterShape("RightPaddle", "ShapeRightPaddle");
        RegisterShape("Back",        "ShapeBack");
        RegisterShape("Start",       "ShapeStart");
        RegisterShape("DPadUp",      "ShapeDPadUp");
        RegisterShape("DPadDown",    "ShapeDPadDown");
        RegisterShape("DPadLeft",    "ShapeDPadLeft");
        RegisterShape("DPadRight",   "ShapeDPadRight");
        RegisterShape("Y",           "ShapeY");
        RegisterShape("A",           "ShapeA");
        RegisterShape("X",           "ShapeX");
        RegisterShape("B",           "ShapeB");
        RegisterShape("ClutchPedal", "ShapeClutchPedal");
        RegisterShape("BrakePedal",  "ShapeBrakePedal");
        RegisterShape("GasPedal",    "ShapeGasPedal");
    }

    private void RegisterShape(string controlId, string elementName)
    {
        var shape = this.FindControl<Control>(elementName);
        if (shape is not null) _shapesByControlId[controlId] = shape;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HighlightedControlIdsProperty) ApplyHighlight();
    }

    private void ApplyHighlight()
    {
        var active = HighlightedControlIds;
        foreach (var (controlId, shape) in _shapesByControlId)
            shape.Classes.Set("controlHeld", active.Contains(controlId));
    }
}
