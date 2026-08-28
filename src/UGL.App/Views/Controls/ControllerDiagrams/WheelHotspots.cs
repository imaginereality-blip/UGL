using Avalonia;

namespace UGL.App.Views.Controls.ControllerDiagrams;

/// <summary>Hit regions for WheelDiagramArt's 360x440 canvas (traced from
/// design_handoff_controller_config/vector-source/wheel.svg). Shifter-set controls
/// (mode-dependent) have no entry here — see the .axaml's comment.</summary>
public static class WheelHotspots
{
    public static readonly IReadOnlyList<ControlHotspot> All =
    [
        new("LeftPaddle",  new Rect(34, 30, 66, 62),   HotspotShape.Rectangle),
        new("RightPaddle", new Rect(260, 30, 66, 62),  HotspotShape.Rectangle),
        new("Back",        new Rect(156, 188, 24, 14), HotspotShape.Rectangle),
        new("Start",       new Rect(192, 188, 24, 14), HotspotShape.Rectangle),
        new("DPadUp",      new Rect(122, 118, 16, 20), HotspotShape.Rectangle),
        new("DPadDown",    new Rect(122, 154, 16, 20), HotspotShape.Rectangle),
        new("DPadLeft",    new Rect(100, 138, 20, 16), HotspotShape.Rectangle),
        new("DPadRight",   new Rect(140, 138, 20, 16), HotspotShape.Rectangle),
        new("Y",           new Rect(220, 110, 24, 24), HotspotShape.Circle),
        new("A",           new Rect(220, 148, 24, 24), HotspotShape.Circle),
        new("X",           new Rect(201, 129, 24, 24), HotspotShape.Circle),
        new("B",           new Rect(239, 129, 24, 24), HotspotShape.Circle),
        new("ClutchPedal", new Rect(98, 330, 34, 80),  HotspotShape.Rectangle),
        new("BrakePedal",  new Rect(163, 330, 34, 80), HotspotShape.Rectangle),
        new("GasPedal",    new Rect(228, 330, 34, 80), HotspotShape.Rectangle),
    ];
}
