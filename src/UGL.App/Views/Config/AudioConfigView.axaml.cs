using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using UGL.App.ViewModels.Config;

namespace UGL.App.Views.Config;

public sealed partial class AudioConfigView : UserControl
{
    // Custom drag-data key - an arbitrary string works fine as a format name for
    // in-process drag-and-drop (this never needs to cross a process boundary).
    private const string TrackIdFormat = "UGL.AudioTrackId";

    // Guards against re-entering DoDragDrop on every PointerMoved tick while a drag
    // is already in progress for that list.
    private bool _isDraggingAvailable;
    private bool _isDraggingAssigned;

    public AudioConfigView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Drag-and-drop for mouse users, alongside the existing controller
        // sub-mode (Confirm/Up/Down) which drives the exact same underlying
        // AssignTrack/UnassignTrack methods either way.
        //
        // Hooked on PointerMoved (checking the left button is still held), not
        // PointerPressed — official Avalonia guidance explicitly warns against
        // starting a drag on every PointerPressed, since a plain click would
        // immediately trigger one and conflict with the ListBox's own selection
        // handling. PointerMoved-while-held is the confirmed-working pattern for
        // this Avalonia version.
        DragDrop.SetAllowDrop(AvailableTracksList, true);
        DragDrop.SetAllowDrop(AssignedTracksList, true);

        AvailableTracksList.AddHandler(PointerMovedEvent, OnAvailablePointerMoved, handledEventsToo: false);
        AssignedTracksList.AddHandler(PointerMovedEvent, OnAssignedPointerMoved, handledEventsToo: false);

        AvailableTracksList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AssignedTracksList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AvailableTracksList.AddHandler(DragDrop.DropEvent, OnDropOnAvailable);
        AssignedTracksList.AddHandler(DragDrop.DropEvent, OnDropOnAssigned);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is AudioConfigViewModel vm)
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

    // ── Drag source: Available pane ─────────────────────────────────────────
    private async void OnAvailablePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDraggingAvailable) return;
        if (!e.GetCurrentPoint(AvailableTracksList).Properties.IsLeftButtonPressed) return;
        if (AvailableTracksList.SelectedItem is not PlaylistTrackItem item) return;

        _isDraggingAvailable = true;
        try
        {
            var data = new DataObject();
            data.Set(TrackIdFormat, item.Id);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        finally { _isDraggingAvailable = false; }
    }

    // ── Drag source: Assigned pane ───────────────────────────────────────────
    private async void OnAssignedPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDraggingAssigned) return;
        if (!e.GetCurrentPoint(AssignedTracksList).Properties.IsLeftButtonPressed) return;
        if (AssignedTracksList.SelectedItem is not PlaylistTrackItem item) return;

        _isDraggingAssigned = true;
        try
        {
            var data = new DataObject();
            data.Set(TrackIdFormat, item.Id);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        finally { _isDraggingAssigned = false; }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(TrackIdFormat) ? DragDropEffects.Move : DragDropEffects.None;
    }

    // Dropping onto Available always means "remove from playlist" - only meaningful
    // if the dragged track actually came from Assigned; ViewModel.UnassignTrack is a
    // no-op if the Id isn't currently in AssignedTracks, so no extra check needed here.
    private void OnDropOnAvailable(object? sender, DragEventArgs e)
    {
        if (e.Data.Get(TrackIdFormat) is string trackId && DataContext is AudioConfigViewModel vm)
            vm.UnassignTrack(trackId);
    }

    // Dropping onto Assigned always means "add to playlist" - same reasoning as above.
    private void OnDropOnAssigned(object? sender, DragEventArgs e)
    {
        if (e.Data.Get(TrackIdFormat) is string trackId && DataContext is AudioConfigViewModel vm)
            vm.AssignTrack(trackId);
    }
}
