using Avalonia;

namespace UGL.App.Views.Controls.ControllerDiagrams;

/// <summary>Hit regions for LightgunDiagramArt's 560x217.1 canvas — solved directly
/// from design_handoff_controller_config/vector-source/lightgun-fixed.svg's real
/// element coordinates (ellipse cx/cy, rect x/y/w/h, and for the rotated D-pad
/// triangles, each one's rotate(angle,cx,cy) transform applied to its base centroid),
/// scaled by the same 560/1715 factor used to size the rendered SVG. Mirrors the
/// overlay shapes' Canvas.Left/Top/Width/Height in LightgunDiagramArt.axaml.</summary>
public static class LightgunHotspots
{
    public static readonly IReadOnlyList<ControlHotspot> All =
    [
        new("DPadUp",       new Rect(24.68, 21.80, 14, 14), HotspotShape.Circle),
        new("DPadDown",     new Rect(24.91, 36.87, 14, 14), HotspotShape.Circle),
        new("DPadLeft",     new Rect(17.02, 28.99, 14, 14), HotspotShape.Circle),
        new("DPadRight",    new Rect(32.35, 29.04, 14, 14), HotspotShape.Circle),
        new("Button6",      new Rect(176.87, 60.86, 16, 16), HotspotShape.Circle),
        new("Button4",      new Rect(192.91, 61.74, 16, 16), HotspotShape.Circle),
        new("Button5",      new Rect(226.10, 56.06, 16, 16), HotspotShape.Circle),
        new("Button7",      new Rect(304.51, 59.09, 16, 12), HotspotShape.Rectangle),
        new("Button10",     new Rect(315.71, 47.81, 12, 16), HotspotShape.Rectangle),
        new("Button8",      new Rect(315.72, 66.42, 12, 16), HotspotShape.Rectangle),
        new("Button9",      new Rect(322.75, 59.45, 16, 12), HotspotShape.Rectangle),
        new("Button2",      new Rect(346.24, 61.81, 16, 16), HotspotShape.Circle),
        new("Button1",      new Rect(363.90, 60.78, 16, 16), HotspotShape.Circle),
        new("Button3",      new Rect(384.83, 58.58, 16, 16), HotspotShape.Circle),
        new("MouseLeft",    new Rect(415.58, 66.32, 18, 18), HotspotShape.Circle),
        new("MouseRight",   new Rect(439.92, 65.61, 16, 16), HotspotShape.Circle),
        new("MouseMiddle",  new Rect(441.60, 43.93, 22, 12), HotspotShape.Rectangle),
    ];
}
