using Avalonia;
using Avalonia.Controls;

namespace UGL.App.Views.Controls.ControllerDiagrams;

/// <summary>
/// Original vector reference art for an Xbox-style gamepad — see the .axaml for the
/// layout reasoning. Named shapes correspond 1:1 to ControllerControlDefinitions.
/// Gamepad's ControlIds ("ShapeLeftBumper" for "LeftBumper", etc.) so
/// GamepadHotspots/ControllerHotspotOverlay and this art always agree on what each
/// region represents.
/// </summary>
public sealed partial class GamepadDiagramArt : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<string>> HighlightedControlIdsProperty =
        AvaloniaProperty.Register<GamepadDiagramArt, IReadOnlyList<string>>(
            nameof(HighlightedControlIds), defaultValue: []);

    public IReadOnlyList<string> HighlightedControlIds
    {
        get => GetValue(HighlightedControlIdsProperty);
        set => SetValue(HighlightedControlIdsProperty, value);
    }

    private readonly Dictionary<string, Control> _shapesByControlId = new();

    public GamepadDiagramArt()
    {
        InitializeComponent();
        RegisterShape("LeftTrigger",  "ShapeLeftTrigger");
        RegisterShape("RightTrigger", "ShapeRightTrigger");
        RegisterShape("LeftBumper",   "ShapeLeftBumper");
        RegisterShape("RightBumper",  "ShapeRightBumper");
        RegisterShape("L3",           "ShapeL3");
        RegisterShape("R3",           "ShapeR3");
        RegisterShape("DPadUp",       "ShapeDPadUp");
        RegisterShape("DPadDown",     "ShapeDPadDown");
        RegisterShape("DPadLeft",     "ShapeDPadLeft");
        RegisterShape("DPadRight",    "ShapeDPadRight");
        RegisterShape("A",            "ShapeA");
        RegisterShape("B",            "ShapeB");
        RegisterShape("X",            "ShapeX");
        RegisterShape("Y",            "ShapeY");
        RegisterShape("View",         "ShapeView");
        RegisterShape("Menu",         "ShapeMenu");
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
