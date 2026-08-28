using Avalonia;

namespace UGL.App.Views.Controls.ControllerDiagrams;

public enum HotspotShape { Circle, Rectangle }

/// <summary>
/// One clickable hit-region over a diagram, kept entirely separate from the art
/// itself (GamepadDiagramArt etc.) so hotspot placement can be tuned independently of
/// the vector art — per the design handoff's explicit ask for "a separate control-id →
/// hotspot map, not hand-drawn SVG in the view." Bounds are in the SAME fixed
/// coordinate space as the corresponding *DiagramArt control's Canvas (360x230 for
/// Gamepad, see each Hotspots file for its own canvas size).
/// </summary>
public sealed record ControlHotspot(string ControlId, Rect Bounds, HotspotShape Shape);
