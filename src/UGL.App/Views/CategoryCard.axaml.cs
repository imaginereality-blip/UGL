using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UGL.App.Services;
using UGL.App.ViewModels;

namespace UGL.App.Views;

public sealed partial class CategoryCard : UserControl
{
    // Store control references directly — avoids FindControl returning null
    private Border?    _cardBorder;
    private Image?     _coverImage;
    private TextBlock? _placeholder;
    private Grid?       _titleGraphicContainer;
    private Grid?       _titleGraphicCell;
    private Image?       _titleGraphicBakedImage;
    private Viewbox?     _titleGraphicLiveFallback;

    // CategoryCard is instantiated by Avalonia's DataTemplate system, not the DI
    // container (same reasoning as TitleGraphicsSettings' own doc comment), so the
    // baker is resolved lazily from the app-wide service locator instead of being
    // constructor-injected.
    private static TitleGraphicsBaker? Baker =>
        App.Services?.GetService<TitleGraphicsBaker>();

    private CategoryCardViewModel? _vm;
    private System.ComponentModel.PropertyChangedEventHandler? _handler;
    private DispatcherTimer? _pulseTimer;
    private DateTime _pulseStart;

    public CategoryCard()
    {
        InitializeComponent();

        // Cache control references immediately after InitializeComponent
        // At this point the visual tree is fully built
        _cardBorder   = this.FindControl<Border>("CardBorder");
        _coverImage   = this.FindControl<Image>("CoverImage");
        _placeholder  = this.FindControl<TextBlock>("PlaceholderText");
        _titleGraphicContainer = this.FindControl<Grid>("TitleGraphicContainer");
        _titleGraphicCell      = this.FindControl<Grid>("TitleGraphicCell");
        _titleGraphicBakedImage   = this.FindControl<Image>("TitleGraphicBakedImage");
        _titleGraphicLiveFallback = this.FindControl<Viewbox>("TitleGraphicLiveFallback");

        DataContextChanged += OnDataContextChanged;

        // Subscribed once here, not per DataContext change — the card pool is small
        // and bounded (five instances), so one subscription for the control's lifetime
        // is safe and avoids stacking duplicate handlers on every VM reassignment.
        CardHighlightSettings.Changed += OnHighlightSettingsChanged;
        TitleGraphicsSettings.Changed += OnTitleGraphicsSettingsChanged;
        if (Baker is { } baker) baker.CategoryBaked += OnCategoryBaked;

        if (_cardBorder is not null)
            _cardBorder.SizeChanged += OnCardSizeChanged;
    }

    private void OnCardSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        CardDimensionInfo.ReportCategoryCardSize(new Size(e.NewSize.Width * scaling, e.NewSize.Height * scaling));

        // The Bottom-placement downward nudge is sized as a fraction of the card's
        // actual height (see ApplyTitleGraphic), so it needs recomputing whenever
        // that changes — e.g. the first layout pass after construction, when Bounds
        // wasn't established yet.
        ApplyTitleGraphic();
    }

    private void OnHighlightSettingsChanged()
    {
        if (_vm is not null) ApplySelectionStyle(_vm.IsSelected);
    }

    private void OnTitleGraphicsSettingsChanged() => ApplyTitleGraphic();

    /// <summary>Fires on TitleGraphicsBaker's own pump thread — marshal back to the UI
    /// thread before touching any Avalonia control.</summary>
    private void OnCategoryBaked(string categoryId)
    {
        if (_vm?.Category?.Id != categoryId) return;
        Dispatcher.UIThread.Post(UpdateTitleGraphicSource);
    }

    /// <summary>
    /// Shows/hides and repositions the category-title text overlay per Settings →
    /// Title Graphics. Only visibility/scale/position are handled here —
    /// TitleGraphicsSettings is a static live-settings bridge (see its own doc
    /// comment), same reasoning as CardHighlightSettings/ApplySelectionStyle above.
    /// The text itself is a plain XAML binding (Category.Label, uppercased), not set
    /// here — see CategoryCard.axaml.
    /// </summary>
    private void ApplyTitleGraphic()
    {
        if (_titleGraphicContainer is null || _titleGraphicCell is null) return;

        _titleGraphicContainer.IsVisible = TitleGraphicsSettings.Enabled;
        if (!TitleGraphicsSettings.Enabled) return;

        // Scale applied here, after Viewbox's own auto-fit — a scale set *inside* the
        // Viewbox would just get normalized back out, since Viewbox always fits its
        // child to the same box regardless of the child's own scale. PositionX/Y are
        // percentages of the card's actual rendered size (not fixed pixels), replacing
        // the old fixed Top/Middle/Bottom placement with continuous, user-adjustable
        // sliders (Settings → Title Graphics).
        var cardWidth = _cardBorder?.Bounds.Width ?? 0;
        var cardHeight = _cardBorder?.Bounds.Height ?? 0;
        var translateX = TitleGraphicsSettings.PositionX / 100.0 * cardWidth;
        var translateY = TitleGraphicsSettings.PositionY / 100.0 * cardHeight;
        _titleGraphicCell.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(TitleGraphicsSettings.Scale, TitleGraphicsSettings.Scale),
                new TranslateTransform(translateX, translateY),
            },
        };

        UpdateTitleGraphicSource();
    }

    /// <summary>
    /// Prefers the real 3D bake (TitleGraphicsBaker — see its own doc comment) when a
    /// fresh PNG exists for this category; otherwise shows the live 2D fallback
    /// (CategoryTitleGraphic). "Fresh" means the cached PNG's label and style hash both
    /// still match current settings — a stale one is treated the same as missing, since
    /// a re-bake for it may already be running in the background.
    /// </summary>
    private void UpdateTitleGraphicSource()
    {
        if (_titleGraphicBakedImage is null || _titleGraphicLiveFallback is null) return;

        var categoryId = _vm?.Category?.Id;
        var label = _vm?.Category?.Label;
        var baker = Baker;

        if (baker is null || string.IsNullOrWhiteSpace(categoryId) || string.IsNullOrWhiteSpace(label))
        {
            _titleGraphicBakedImage.IsVisible = false;
            _titleGraphicLiveFallback.IsVisible = true;
            return;
        }

        var cachedPath = baker.GetCachedImageIfFresh(categoryId, label);
        if (cachedPath is null)
        {
            _titleGraphicBakedImage.IsVisible = false;
            _titleGraphicLiveFallback.IsVisible = true;
            return;
        }

        try
        {
            // Loaded fresh each time rather than cached on the VM — bake files change
            // rarely (only on category save or a global style re-bake) and this control
            // pool is small (five instances), so re-decoding on each ApplyTitleGraphic
            // call is cheap and avoids holding a stale Bitmap across re-bakes.
            using var stream = File.OpenRead(cachedPath);
            _titleGraphicBakedImage.Source = new Bitmap(stream);
            _titleGraphicBakedImage.IsVisible = true;
            _titleGraphicLiveFallback.IsVisible = false;
        }
        catch
        {
            // Corrupt/partially-written PNG (e.g. read mid-bake) — fall back safely
            // rather than showing a broken image.
            _titleGraphicBakedImage.IsVisible = false;
            _titleGraphicLiveFallback.IsVisible = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null && _handler is not null)
        {
            _vm.PropertyChanged -= _handler;
            _handler = null;
        }

        _vm = DataContext as CategoryCardViewModel;
        if (_vm is null) return;

        _handler = (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(CategoryCardViewModel.IsSelected):
                    ApplySelectionStyle(_vm.IsSelected);
                    break;
                case nameof(CategoryCardViewModel.CoverBitmap):
                    SetCoverImage(_vm.CoverBitmap);
                    break;
                case nameof(CategoryCardViewModel.Category):
                    SetCoverImage(null);
                    ApplyTitleGraphic();
                    break;
            }
        };

        _vm.PropertyChanged += _handler;
        ApplySelectionStyle(_vm.IsSelected);
        SetCoverImage(_vm.CoverBitmap);
        ApplyTitleGraphic();
    }

    private void ApplySelectionStyle(bool isSelected)
    {
        if (_cardBorder is null) return;

        StopPulse();

        if (!isSelected)
        {
            _cardBorder.BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF"));
            _cardBorder.BorderThickness = new Thickness(0);
            _cardBorder.Effect = null;
            return;
        }

        var baseColor = ParseColorSafe(CardHighlightSettings.Color);
        _cardBorder.BorderThickness = new Thickness(CardHighlightSettings.Thickness);

        if (string.Equals(CardHighlightSettings.Style, "Pulsing", StringComparison.OrdinalIgnoreCase))
            StartPulse(_cardBorder, baseColor);
        else
        {
            _cardBorder.BorderBrush = new SolidColorBrush(baseColor) { Opacity = CardHighlightSettings.Intensity };
            _cardBorder.Effect = MakeGlow(baseColor, CardHighlightSettings.Intensity);
        }
    }

    /// <summary>
    /// Soft glow kept alongside (not replacing) the existing selection border — reads
    /// as the card being lit from behind rather than a rectangle drawn on top of it.
    /// </summary>
    private static DropShadowEffect MakeGlow(Color baseColor, double intensity) => new()
    {
        Color = baseColor,
        BlurRadius = 24,
        OffsetX = 0,
        OffsetY = 0,
        Opacity = Math.Clamp(intensity * 0.65, 0, 1),
    };

    private void StartPulse(Border b, Color baseColor)
    {
        _pulseStart = DateTime.UtcNow;
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _pulseTimer.Tick += (_, _) =>
        {
            var elapsedSeconds = (DateTime.UtcNow - _pulseStart).TotalSeconds;
            double wave = (Math.Sin(elapsedSeconds * Math.PI * 1.3) + 1) / 2; // 0..1
            double opacity = Math.Clamp((0.3 + wave * 0.7) * CardHighlightSettings.Intensity, 0, 1);
            b.BorderBrush = new SolidColorBrush(baseColor) { Opacity = opacity };
            b.Effect = MakeGlow(baseColor, opacity);
        };
        _pulseTimer.Start();
    }

    private void StopPulse()
    {
        if (_pulseTimer is null) return;
        _pulseTimer.Stop();
        _pulseTimer = null;
    }

    private static Color ParseColorSafe(string hex)
    {
        try { return Color.Parse(hex); }
        catch { return Color.Parse("#FFFFD700"); }
    }

    private void SetCoverImage(Bitmap? bitmap)
    {
        if (_coverImage is null) return;

        if (bitmap is not null)
        {
            _coverImage.Source    = bitmap;
            _coverImage.Stretch   = Stretch.Fill;
            _coverImage.IsVisible = true;

            if (_placeholder  is not null) _placeholder.IsVisible  = false;
        }
        else
        {
            _coverImage.Source    = null;
            _coverImage.IsVisible = false;

            if (_placeholder  is not null) _placeholder.IsVisible  = true;
        }
    }
}
