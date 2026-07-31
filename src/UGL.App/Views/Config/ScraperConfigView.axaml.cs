using Avalonia.Controls;
using Avalonia.Platform.Storage;
using UGL.App.ViewModels.Config;

namespace UGL.App.Views.Config;

public sealed partial class ScraperConfigView : UserControl
{
    public ScraperConfigView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ScraperConfigViewModel vm)
            vm.BrowseFileRequested += OnBrowseFileRequestedAsync;
    }

    private async Task<string?> OnBrowseFileRequestedAsync(string filterName, string[] patterns)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storage || !storage.CanOpen) return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select File",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = patterns }],
        });

        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }
}
