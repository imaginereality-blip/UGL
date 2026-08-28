using Avalonia.Controls;
using UGL.App.ViewModels.Config;
using UGL.App.Views.Controls.ControllerDiagrams;

namespace UGL.App.Views.Config;

public sealed partial class ControllersConfigView : UserControl
{
    public ControllersConfigView()
    {
        InitializeComponent();

        var overlay = this.FindControl<ControllerHotspotOverlay>("HotspotOverlay");
        if (overlay is not null)
            overlay.HotspotClicked += (_, controlId) =>
            {
                if (DataContext is ControllersConfigViewModel vm) vm.OnHotspotClicked(controlId);
            };
    }
}
