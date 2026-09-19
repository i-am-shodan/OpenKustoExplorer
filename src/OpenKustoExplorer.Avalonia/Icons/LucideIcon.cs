using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.VisualTree;

[assembly: XmlnsDefinition("https://github.com/avaloniaui", "Lucide.Avalonia")]

namespace Lucide.Avalonia;

/// <summary>
/// Renders the subset of Lucide icons used by Open Kusto Explorer without runtime compression.
/// </summary>
public class LucideIcon : Control
{
    /// <summary>
    /// Identifies the <see cref="Size"/> styled property.
    /// </summary>
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<LucideIcon, double>(nameof(Size), RawIconSize);

    /// <summary>
    /// Identifies the <see cref="Foreground"/> styled property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LucideIcon>();

    /// <summary>
    /// Identifies the <see cref="StrokeWidth"/> styled property.
    /// </summary>
    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<LucideIcon, double>(nameof(StrokeWidth), 1.8);

    /// <summary>
    /// Identifies the <see cref="Kind"/> styled property.
    /// </summary>
    public static readonly StyledProperty<LucideIconKind?> KindProperty =
        AvaloniaProperty.Register<LucideIcon, LucideIconKind?>(nameof(Kind));

    private const double RawIconSize = 24;
    private Pen? stroke;
    private Geometry? geometry;

    static LucideIcon()
    {
        AffectsRender<LucideIcon>(KindProperty, SizeProperty, ForegroundProperty, StrokeWidthProperty);
        AffectsMeasure<LucideIcon>(SizeProperty);
    }

    /// <summary>
    /// Gets or sets the square icon size.
    /// </summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon stroke brush.
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the source-coordinate stroke width.
    /// </summary>
    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon geometry kind.
    /// </summary>
    public LucideIconKind? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (geometry is null || stroke is null)
        {
            return;
        }

        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        double scale = Size / RawIconSize;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(null, stroke, geometry);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == KindProperty)
        {
            LucideIconKind? kind = change.GetNewValue<LucideIconKind?>();
            geometry = kind is null ? null : LucideIconGeometry.Get(kind.Value);
        }
        else if (change.Property == ForegroundProperty || change.Property == StrokeWidthProperty)
        {
            UpdateStroke();
        }

        base.OnPropertyChanged(change);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => GetIconSize();

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize) => GetIconSize();

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateStroke();
        if (geometry is null && Kind is LucideIconKind kind)
        {
            geometry = LucideIconGeometry.Get(kind);
        }
    }

    private Size GetIconSize() => new(Size, Size);

    private void UpdateStroke()
    {
        if (stroke is null)
        {
            stroke = new Pen(Foreground, StrokeWidth, null, PenLineCap.Round, PenLineJoin.Round);
        }
        else
        {
            stroke.Brush = Foreground;
            stroke.Thickness = StrokeWidth;
        }
    }
}
