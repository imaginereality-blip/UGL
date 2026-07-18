using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UGL.Core.Models;
using UGL.Media;

namespace UGL.App.ViewModels;

/// <summary>
/// Represents one full-height category card on the Home Menu.
///
/// Live reload fix: CoverBitmap setter forces OnPropertyChanged even when
/// the bitmap reference is the same object — ObservableObject skips the
/// notification if the value hasn't changed by reference, which prevents
/// the code-behind from updating ApplyCoverVisibility on reload.
/// </summary>
public sealed partial class CategoryCardViewModel : ObservableObject
{
    [ObservableProperty] private Category _category;
    [ObservableProperty] private bool _isSelected;

    // Not using [ObservableProperty] — we need to force notification always
    private Bitmap? _coverBitmap;
    public Bitmap? CoverBitmap
    {
        get => _coverBitmap;
        set
        {
            _coverBitmap = value;
            // Always raise — even if same reference, so live reload triggers
            // the code-behind's ApplyCoverVisibility
            OnPropertyChanged(nameof(CoverBitmap));
        }
    }

    public CategoryCardViewModel(Category category)
    {
        _category = category;
    }

    public string Label => Category.Label;
    public string Id    => Category.Id;

    /// <summary>
    /// The big background letter shown when there's no cover art. Skips past emoji
    /// and other symbols to find the first actual letter/digit in the Label, rather
    /// than blindly using Label[0] — a Label starting with an emoji (as the built-in
    /// Favorites category's did briefly) could render blank or as an unsupported
    /// glyph at the large placeholder font size, making the card look empty/skipped
    /// even though it was actually selected correctly.
    /// </summary>
    public string PlaceholderLetter
    {
        get
        {
            foreach (var ch in Label)
                if (char.IsLetterOrDigit(ch))
                    return ch.ToString();
            return Label.Length > 0 ? Label[0].ToString() : "?";
        }
    }

    partial void OnCategoryChanged(Category value)
    {
        _coverBitmap = null;
        OnPropertyChanged(nameof(CoverBitmap));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(PlaceholderLetter));
    }

    public async Task LoadCoverAsync(
        SkiaMediaCache cache,
        MediaAssetResolver resolver,
        CancellationToken ct = default)
    {
        var path = resolver.ResolveCategoryArt(Category);
        if (path is null) return;

        var bitmap = await cache.GetBitmapAsync(path, ct);
        if (bitmap is null || ct.IsCancellationRequested) return;

        if (Dispatcher.UIThread.CheckAccess())
            CoverBitmap = bitmap;
        else
            Dispatcher.UIThread.Post(() => CoverBitmap = bitmap, DispatcherPriority.Render);
    }
}
