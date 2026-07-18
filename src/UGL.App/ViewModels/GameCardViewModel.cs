using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UGL.Core.Models;
using UGL.Media;

namespace UGL.App.ViewModels;

public sealed partial class GameCardViewModel : ObservableObject
{
    public Game Game { get; }
    public string SystemName { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _videoPath;
    [ObservableProperty] private bool _isVideoActive;

    // Force notification always — same fix as CategoryCardViewModel
    private Bitmap? _coverBitmap;
    public Bitmap? CoverBitmap
    {
        get => _coverBitmap;
        set
        {
            _coverBitmap = value;
            OnPropertyChanged(nameof(CoverBitmap));
        }
    }

    public GameCardViewModel(Game game, string systemName)
    {
        Game = game;
        SystemName = systemName;
    }

    public string Title   => Game.Title;
    public string Players => Game.Players == 1 ? "1 Player" : $"{Game.Players} Players";

    /// <summary>
    /// Game is a plain mutable object, not itself observable — toggling Game.IsFavorite
    /// directly won't raise PropertyChanged on its own, so NotifyFavoriteChanged() must
    /// be called explicitly after changing it (see GameBrowserViewModel.ToggleFavoriteOnSelectedAsync).
    /// </summary>
    public bool IsFavorite => Game.IsFavorite;
    public void NotifyFavoriteChanged() => OnPropertyChanged(nameof(IsFavorite));

    public async Task LoadCoverAsync(
        SkiaMediaCache cache,
        MediaAssetResolver resolver,
        CancellationToken ct = default)
    {
        var path = resolver.ResolveCover(Game);
        if (path is null) return;

        var bitmap = await cache.GetBitmapAsync(path, ct);
        if (bitmap is null || ct.IsCancellationRequested) return;

        if (Dispatcher.UIThread.CheckAccess())
            CoverBitmap = bitmap;
        else
            Dispatcher.UIThread.Post(() => CoverBitmap = bitmap, DispatcherPriority.Render);
    }
}
