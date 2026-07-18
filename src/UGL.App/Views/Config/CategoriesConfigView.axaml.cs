using Avalonia.Controls;
using Avalonia.Platform.Storage;
using UGL.App.ViewModels.Config;

namespace UGL.App.Views.Config;

public sealed partial class CategoriesConfigView : UserControl
{
    public CategoriesConfigView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not CategoriesConfigViewModel vm) return;

        vm.BrowseFileRequested += async (title, patterns) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return null;

            var filters = new List<FilePickerFileType>
            {
                new(title) { Patterns = patterns }
            };

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = title,
                    FileTypeFilter = filters,
                    AllowMultiple = false
                });

            if (files.Count == 0) return null;

            var file = files[0];
            var path = file.TryGetLocalPath();
            if (path is null && file.Path is { IsAbsoluteUri: true } uri)
                path = uri.LocalPath;

            return path;
        };
    }
}
