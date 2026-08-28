using Avalonia;

namespace UGL.App.Views.Controls.ControllerDiagrams;

/// <summary>Hit regions for GamepadDiagramArt's 440x300 canvas — bounds mirror each
/// named shape's Canvas.Left/Top/Width/Height exactly (traced from
/// design_handoff_controller_config/vector-source/gamepad.svg), so a click lands on
/// the same area the highlight ring draws around.</summary>
public static class GamepadHotspots
{
    public static readonly IReadOnlyList<ControlHotspot> All =
    [
        new("LeftTrigger",  new Rect(102, 8, 56, 20),     HotspotShape.Rectangle),
        new("RightTrigger", new Rect(282, 8, 56, 20),     HotspotShape.Rectangle),
        new("LeftBumper",   new Rect(88, 30, 80, 20),     HotspotShape.Rectangle),
        new("RightBumper",  new Rect(280, 30, 80, 20),    HotspotShape.Rectangle),
        new("View",         new Rect(178, 102, 24, 15),   HotspotShape.Rectangle),
        new("Menu",         new Rect(238, 102, 24, 15),   HotspotShape.Rectangle),
        new("L3",           new Rect(86.56, 77.99, 60, 60), HotspotShape.Circle),
        new("R3",           new Rect(249.84, 149.47, 60, 60), HotspotShape.Circle),
        new("DPadUp",       new Rect(164.93, 148.91, 13.95, 20.42), HotspotShape.Rectangle),
        new("DPadDown",     new Rect(165.02, 186.49, 13.95, 20.42), HotspotShape.Rectangle),
        new("DPadLeft",     new Rect(144.66, 170.33, 18.61, 15.31), HotspotShape.Rectangle),
        new("DPadRight",    new Rect(180.75, 170.33, 18.61, 15.31), HotspotShape.Rectangle),
        new("Y",             new Rect(315.82, 74.88, 23.21, 23.21), HotspotShape.Circle),
        new("A",             new Rect(315.82, 120.41, 23.21, 23.21), HotspotShape.Circle),
        new("X",             new Rect(292.17, 97.65, 23.21, 23.21), HotspotShape.Circle),
        new("B",             new Rect(339.48, 97.65, 23.21, 23.21), HotspotShape.Circle),
    ];
}
