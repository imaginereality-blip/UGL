using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UGL.App.ViewModels;

namespace UGL.App.Views;

public sealed partial class CategoryCard : UserControl
{
    // Store control references directly — avoids FindControl returning null
    private Border?    _cardBorder;
    private Image?     _coverImage;
    private TextBlock? _placeholder;

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

        DataContextChanged += OnDataContextChanged;

        // Subscribed once here, not per DataContext change — the card pool is small
        // and bounded (five instances), so one subscription for the control's lifetime
        // is safe and avoids stacking duplicate handlers on every VM reassignment.
        CardHighlightSettings.Changed += OnHighlightSettingsChanged;

        if (_cardBorder is not null)
            _cardBorder.SizeChanged += OnCardSizeChanged;
    }

    private void OnCardSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        CardDimensionInfo.ReportCategoryCardSize(new Size(e.NewSize.Width * scaling, e.NewSize.Height * scaling));
    }

    private void OnHighlightSettingsChanged()
    {
        if (_vm is not null) ApplySelectionStyle(_vm.IsSelected);
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
                    break;
            }
        };

        _vm.PropertyChanged += _handler;
        ApplySelectionStyle(_vm.IsSelected);
        SetCoverImage(_vm.CoverBitmap);
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
