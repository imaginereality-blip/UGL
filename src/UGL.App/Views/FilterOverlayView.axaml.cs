using Avalonia.Controls;

namespace UGL.App.Views;

/// <summary>
/// Code-behind for FilterOverlayView.
/// Visual updates are handled entirely by converter bindings on IsFocused/IsSelected
/// (PillStyleConverters.cs) and ObservableCollection on FilterRowViewModel.Options.
/// No manual PropertyChanged wiring needed here.
/// </summary>
public sealed partial class FilterOverlayView : UserControl
{
    public FilterOverlayView()
    {
        InitializeComponent();
    }
}
