using Avalonia;
using Avalonia.Controls;

namespace UGL.App.Views.Controls.ControllerDiagrams;

/// <summary>Vector reference art for a generic arcade lightgun — see the .axaml for
/// layout reasoning. Named shapes correspond 1:1 to ControllerControlDefinitions.
/// Lightgun's ControlIds.</summary>
public sealed partial class LightgunDiagramArt : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<string>> HighlightedControlIdsProperty =
        AvaloniaProperty.Register<LightgunDiagramArt, IReadOnlyList<string>>(
            nameof(HighlightedControlIds), defaultValue: []);

    public IReadOnlyList<string> HighlightedControlIds
    {
        get => GetValue(HighlightedControlIdsProperty);
        set => SetValue(HighlightedControlIdsProperty, value);
    }

    private readonly Dictionary<string, Control> _shapesByControlId = new();

    public LightgunDiagramArt()
    {
        InitializeComponent();
        RegisterShape("DPadUp",      "ShapeDPadUp");
        RegisterShape("DPadDown",    "ShapeDPadDown");
        RegisterShape("DPadLeft",    "ShapeDPadLeft");
        RegisterShape("DPadRight",   "ShapeDPadRight");
        RegisterShape("Button1",     "ShapeButton1");
        RegisterShape("Button2",     "ShapeButton2");
        RegisterShape("Button3",     "ShapeButton3");
        RegisterShape("Button4",     "ShapeButton4");
        RegisterShape("Button5",     "ShapeButton5");
        RegisterShape("Button6",     "ShapeButton6");
        RegisterShape("Button7",     "ShapeButton7");
        RegisterShape("Button8",     "ShapeButton8");
        RegisterShape("Button9",     "ShapeButton9");
        RegisterShape("Button10",    "ShapeButton10");
        RegisterShape("MouseLeft",   "ShapeMouseLeft");
        RegisterShape("MouseRight",  "ShapeMouseRight");
        RegisterShape("MouseMiddle", "ShapeMouseMiddle");
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
