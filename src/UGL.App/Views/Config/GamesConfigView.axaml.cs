using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using UGL.App.ViewModels.Config;

namespace UGL.App.Views.Config;

public sealed partial class GamesConfigView : UserControl
{
    public GamesConfigView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not GamesConfigViewModel vm) return;

        // Wire the file browse event — the VM raises it, we handle the dialog
        vm.BrowseFileRequested += async (title, patterns) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return null;

            var filters = new List<FilePickerFileType>
            {
                new(title) { Patterns = patterns }
            };

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions { Title = title, FileTypeFilter = filters, AllowMultiple = false });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        };

        // Wire drag-drop for each media drop zone
        WireDropZone("CoverDropZone",   "cover",      vm);
        WireDropZone("BgDropZone",      "background", vm);
        WireDropZone("LogoDropZone",    "logo",       vm);
        WireDropZone("VideoDropZone",   "video",      vm);
        WireDropZone("MarqueeDropZone", "marquee",    vm);
    }

    private void WireDropZone(string controlName, string fieldName, GamesConfigViewModel vm)
    {
        if (this.FindControl<Grid>(controlName) is not { } zone) return;

        zone.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        });

        zone.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (!e.Data.Contains(DataFormats.Files)) return;
            var files = e.Data.GetFiles();
            if (files is null) return;
            var first = files.FirstOrDefault()?.TryGetLocalPath();
            if (first is not null) vm.AcceptMediaDrop(fieldName, first);
        });

        DragDrop.SetAllowDrop(zone, true);
    }
}
