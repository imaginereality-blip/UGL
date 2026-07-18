using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UGL.App.ViewModels.Config;

namespace UGL.App.Views.Config;

public sealed partial class PathsConfigView : UserControl
{
    public PathsConfigView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not PathsConfigViewModel vm) return;

        vm.BrowseFolderRequested += async title =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return null;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = title, AllowMultiple = false });

            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        };
    }

    private void OnClearSystemRomPath(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PathsConfigViewModel vm)
            vm.EditSystemRomPath = string.Empty;
    }
}
