using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ScrollKeeper.Presentation.Controls;

/// <summary>
/// Outline icon: draws a geometry laid out on a 24 x 24 grid (see the <c>*Geometry</c> resources in
/// Themes/WindowChrome.axaml) with a round stroke in the inherited text color, scaled to <see cref="IconSize"/>.
/// Replaces the Segoe Fluent Icons glyphs of the WPF version, which only exist on Windows.
/// </summary>
public class Icon : Control
{
    /// <summary>Size of the grid the geometries are drawn on.</summary>
    private const double GridSize = 24;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    /// <summary>Inherited like the text color, so an icon follows its button's hover and pressed colors.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    /// <summary>Stroke width in grid units (scaled with the icon).</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), 2);

    /// <summary>Width and height of the icon in device-independent pixels.</summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(IconSize), 16);

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ForegroundProperty, StrokeThicknessProperty);
        AffectsMeasure<Icon>(IconSizeProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(IconSize, IconSize);

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || Foreground is not { } brush)
            return;

        var bounds = Bounds;
        var scale = Math.Min(bounds.Width, bounds.Height) / GridSize;
        if (scale <= 0)
            return;

        var offsetX = (bounds.Width - GridSize * scale) / 2;
        var offsetY = (bounds.Height - GridSize * scale) / 2;
        var pen = new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
            context.DrawGeometry(null, pen, data);
    }
}
