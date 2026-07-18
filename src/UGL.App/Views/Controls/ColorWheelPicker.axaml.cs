using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;

namespace UGL.App.Views.Controls;

public sealed partial class ColorWheelPicker : UserControl
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<ColorWheelPicker, double>(
            nameof(Hue), defaultValue: 0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<ColorWheelPicker, double>(
            nameof(Saturation), defaultValue: 1, defaultBindingMode: BindingMode.TwoWay);

    public double Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    private bool _dragging;
    private Ellipse? _wheel;
    private Ellipse? _cursor;

    public ColorWheelPicker()
    {
        InitializeComponent();
        _wheel  = this.FindControl<Ellipse>("WheelEllipse");
        _cursor = this.FindControl<Ellipse>("CursorDot");

        PointerPressed  += OnPointerPressed;
        PointerMoved    += OnPointerMoved;
        PointerReleased += OnPointerReleased;

        UpdateCursorPosition();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HueProperty || change.Property == SaturationProperty)
            UpdateCursorPosition();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = true;
        e.Pointer.Capture(this);
        UpdateFromPointer(e.GetPosition(_wheel ?? (Control)this));
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        UpdateFromPointer(e.GetPosition(_wheel ?? (Control)this));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void UpdateFromPointer(Point pos)
    {
        if (_wheel is null) return;
        double radius = _wheel.Bounds.Width / 2;
        if (radius <= 0) return;

        double dx = pos.X - radius;
        double dy = pos.Y - radius;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        // Clockwise from top — see the orientation note in the XAML.
        double angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
        if (angle < 0) angle += 360;

        Hue = angle;
        Saturation = Math.Clamp(distance / radius, 0, 1);
    }

    private void UpdateCursorPosition()
    {
        if (_wheel is null || _cursor is null) return;
        double radius = _wheel.Bounds.Width / 2;
        if (radius <= 0)
        {
            // Bounds not measured yet on first layout pass — retry once arranged.
            _wheel.LayoutUpdated += OnWheelLayoutUpdated;
            return;
        }

        double angleRad = Hue * Math.PI / 180;
        double distance = Saturation * radius;

        double x = radius + distance * Math.Sin(angleRad);
        double y = radius - distance * Math.Cos(angleRad);

        Canvas.SetLeft(_cursor, x - _cursor.Width / 2);
        Canvas.SetTop(_cursor, y - _cursor.Height / 2);
    }

    private void OnWheelLayoutUpdated(object? sender, EventArgs e)
    {
        if (_wheel is null) return;
        _wheel.LayoutUpdated -= OnWheelLayoutUpdated;
        UpdateCursorPosition();
    }
}
