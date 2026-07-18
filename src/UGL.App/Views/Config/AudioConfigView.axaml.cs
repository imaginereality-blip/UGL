using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using UGL.App.ViewModels.Config;

namespace UGL.App.Views.Config;

public sealed partial class AudioConfigView : UserControl
{
    private static readonly IBrush ActiveTabBrush  = new SolidColorBrush(Color.Parse("#FF0078D4"));
    private static readonly IBrush DefaultTabBrush = new SolidColorBrush(Color.Parse("#33FFFFFF"));

    public AudioConfigView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not AudioConfigViewModel vm) return;

        // Tab highlight via PropertyChanged
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(AudioConfigViewModel.IsMusicTabActive)
                                  or nameof(AudioConfigViewModel.IsSoundsTabActive))
                UpdateTabHighlight(vm);
        };
        UpdateTabHighlight(vm);

        // Multi-file browse for tracks
        vm.BrowseFilesRequested += async (title, patterns) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return [];

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = title,
                    FileTypeFilter = [new(title) { Patterns = patterns }],
                    AllowMultiple = true
                });

            return files
                .Select(f => f.TryGetLocalPath())
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();
        };

        // Single-file browse for sound files
        vm.BrowseFileRequested += async (title, patterns) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return null;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = title,
                    FileTypeFilter = [new(title) { Patterns = patterns }],
                    AllowMultiple = false
                });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        };

        // Drag-and-drop for each sound drop zone
        WireSoundDropZone("NavigateDropZone", "navigate", vm);
        WireSoundDropZone("ConfirmDropZone",  "confirm",  vm);
        WireSoundDropZone("BackDropZone",     "back",     vm);
        WireSoundDropZone("ErrorDropZone",    "error",    vm);
    }

    private void UpdateTabHighlight(AudioConfigViewModel vm)
    {
        if (this.FindControl<Button>("MusicTabBtn") is { } music)
            music.Background = vm.IsMusicTabActive ? ActiveTabBrush : DefaultTabBrush;

        if (this.FindControl<Button>("SoundsTabBtn") is { } sounds)
            sounds.Background = vm.IsSoundsTabActive ? ActiveTabBrush : DefaultTabBrush;
    }

    private void WireSoundDropZone(string controlName, string soundKey, AudioConfigViewModel vm)
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
            var first = e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath();
            if (first is null) return;

            switch (soundKey)
            {
                case "navigate": vm.SoundNavigatePath = first; break;
                case "confirm":  vm.SoundConfirmPath  = first; break;
                case "back":     vm.SoundBackPath     = first; break;
                case "error":    vm.SoundErrorPath    = first; break;
            }
        });

        DragDrop.SetAllowDrop(zone, true);
    }
}
