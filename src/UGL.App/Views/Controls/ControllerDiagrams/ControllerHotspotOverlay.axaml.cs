using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace UGL.App.Views.Controls.ControllerDiagrams;

/// <summary>
/// Builds transparent, clickable hit-regions from a ControlHotspot list and lays them
/// over whichever *DiagramArt is currently shown — see the .axaml. Kept entirely
/// data-driven (no hand-authored shapes here) so a device class's hotspot map can be
/// swapped or retuned (GamepadHotspots.cs etc.) without touching this control.
/// </summary>
public sealed partial class ControllerHotspotOverlay : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<ControlHotspot>> HotspotsProperty =
        AvaloniaProperty.Register<ControllerHotspotOverlay, IReadOnlyList<ControlHotspot>>(
            nameof(Hotspots), defaultValue: []);

    public IReadOnlyList<ControlHotspot> Hotspots
    {
        get => GetValue(HotspotsProperty);
        set => SetValue(HotspotsProperty, value);
    }

    public static readonly StyledProperty<Size> CanvasSizeProperty =
        AvaloniaProperty.Register<ControllerHotspotOverlay, Size>(nameof(CanvasSize));

    /// <summary>Must match the corresponding *DiagramArt control's own declared
    /// Width/Height exactly, or hotspot coordinates (authored in that same space)
    /// land in the wrong place.</summary>
    public Size CanvasSize
    {
        get => GetValue(CanvasSizeProperty);
        set => SetValue(CanvasSizeProperty, value);
    }

    /// <summary>Raised when a hotspot is clicked/tapped — the ViewModel starts a
    /// remap capture for this ControlId, same as the row list's Remap button.</summary>
    public event EventHandler<string>? HotspotClicked;

    private Canvas? _rootCanvas;

    public ControllerHotspotOverlay()
    {
        InitializeComponent();
        _rootCanvas = this.FindControl<Canvas>("RootCanvas");
        Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HotspotsProperty || change.Property == CanvasSizeProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        if (_rootCanvas is null) return;

        _rootCanvas.Children.Clear();
        _rootCanvas.Width  = CanvasSize.Width;
        _rootCanvas.Height = CanvasSize.Height;

        foreach (var hotspot in Hotspots)
        {
            if (string.IsNullOrEmpty(hotspot.ControlId)) continue; // e.g. Lightgun's unbound-by-default Guide still gets a hotspot; only a truly empty id is skipped

            Shape shape = hotspot.Shape == HotspotShape.Circle
                ? new Ellipse { Width = hotspot.Bounds.Width, Height = hotspot.Bounds.Height }
                : new Rectangle { Width = hotspot.Bounds.Width, Height = hotspot.Bounds.Height, RadiusX = 3, RadiusY = 3 };

            shape.Fill = Brushes.Transparent;
            shape.Cursor = new Cursor(StandardCursorType.Hand);
            Canvas.SetLeft(shape, hotspot.Bounds.X);
            Canvas.SetTop(shape, hotspot.Bounds.Y);

            var controlId = hotspot.ControlId;
            shape.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                HotspotClicked?.Invoke(this, controlId);
            };

            _rootCanvas.Children.Add(shape);
        }
    }
}
