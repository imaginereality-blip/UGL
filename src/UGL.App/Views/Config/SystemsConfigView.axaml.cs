using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using UGL.App.ViewModels.Config;

namespace UGL.App.Views.Config;

public sealed partial class SystemsConfigView : UserControl
{
    private static readonly IBrush FoundBrush   = new SolidColorBrush(Color.Parse("#FF44DD88"));
    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.Parse("#FFFF4444"));
    private static readonly IBrush ActiveTab    = new SolidColorBrush(Color.Parse("#FF0078D4"));
    private static readonly IBrush DefaultTab   = new SolidColorBrush(Color.Parse("#33FFFFFF"));

    public SystemsConfigView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SystemsConfigViewModel vm) return;

        vm.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(SystemsConfigViewModel.IsSystemsTabActive):
                case nameof(SystemsConfigViewModel.IsEmulatorsTabActive):
                    UpdateTabHighlight(vm);
                    break;

                case nameof(SystemsConfigViewModel.EditEmulatorExeFound):
                    UpdateExeStatus(vm);
                    break;
            }
        };

        UpdateTabHighlight(vm);

        // File browse dialog for emulator executable
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
    }

    private void UpdateTabHighlight(SystemsConfigViewModel vm)
    {
        if (this.FindControl<Button>("SystemsTabBtn") is { } sys)
            sys.Background = vm.IsSystemsTabActive ? ActiveTab : DefaultTab;

        if (this.FindControl<Button>("EmulatorsTabBtn") is { } emu)
            emu.Background = vm.IsEmulatorsTabActive ? ActiveTab : DefaultTab;
    }

    private void UpdateExeStatus(SystemsConfigViewModel vm)
    {
        var brush = vm.EditEmulatorExeFound ? FoundBrush : MissingBrush;
        if (this.FindControl<TextBlock>("ExeStatusText")   is { } tb1) tb1.Foreground = brush;
        if (this.FindControl<TextBlock>("ExeStatusTextRA") is { } tb2) tb2.Foreground = brush;
    }
}
