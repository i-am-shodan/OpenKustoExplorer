using Avalonia;
using Avalonia.Controls;

namespace OpenKustoExplorer.Desktop.Controls;

/// <summary>
/// Wraps children horizontally while reserving an inset only on the first line.
/// </summary>
public sealed class FirstLineInsetWrapPanel : Panel
{
    /// <summary>
    /// Defines the <see cref="FirstLineInset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> FirstLineInsetProperty =
        AvaloniaProperty.Register<FirstLineInsetWrapPanel, double>(nameof(FirstLineInset), 38d);

    /// <summary>
    /// Defines the <see cref="ItemSpacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<FirstLineInsetWrapPanel, double>(nameof(ItemSpacing), 2d);

    /// <summary>
    /// Defines the <see cref="LineSpacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<FirstLineInsetWrapPanel, double>(nameof(LineSpacing), 2d);

    /// <summary>
    /// Gets or sets the horizontal space reserved before the first child on the first line.
    /// </summary>
    public double FirstLineInset
    {
        get => GetValue(FirstLineInsetProperty);
        set => SetValue(FirstLineInsetProperty, value);
    }

    /// <summary>
    /// Gets or sets horizontal space between adjacent children.
    /// </summary>
    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets vertical space between wrapped lines.
    /// </summary>
    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double availableWidth = NormalizeAvailableWidth(availableSize.Width);
        double currentX = Math.Min(FirstLineInset, availableWidth);
        double currentLineHeight = 0;
        double measuredWidth = currentX;
        double measuredHeight = 0;

        foreach (Control child in Children.Where(child => child.IsVisible))
        {
            child.Measure(new Size(availableWidth, availableSize.Height));
            Size desiredSize = child.DesiredSize;

            if (ShouldWrap(currentX, desiredSize.Width, availableWidth))
            {
                measuredHeight += currentLineHeight + LineSpacing;
                currentX = 0;
                currentLineHeight = 0;
            }

            currentX += desiredSize.Width;
            measuredWidth = Math.Max(measuredWidth, currentX);
            currentLineHeight = Math.Max(currentLineHeight, desiredSize.Height);
            currentX += ItemSpacing;
        }

        measuredHeight += currentLineHeight;
        return new Size(Math.Min(measuredWidth, availableWidth), measuredHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double currentX = Math.Min(FirstLineInset, finalSize.Width);
        double currentY = 0;
        double currentLineHeight = 0;

        foreach (Control child in Children.Where(child => child.IsVisible))
        {
            Size desiredSize = child.DesiredSize;

            if (ShouldWrap(currentX, desiredSize.Width, finalSize.Width))
            {
                currentY += currentLineHeight + LineSpacing;
                currentX = 0;
                currentLineHeight = 0;
            }

            child.Arrange(new Rect(currentX, currentY, desiredSize.Width, desiredSize.Height));
            currentX += desiredSize.Width + ItemSpacing;
            currentLineHeight = Math.Max(currentLineHeight, desiredSize.Height);
        }

        return finalSize;
    }

    private static double NormalizeAvailableWidth(double availableWidth)
    {
        return double.IsInfinity(availableWidth) ? double.MaxValue : Math.Max(0, availableWidth);
    }

    private static bool ShouldWrap(double currentX, double childWidth, double availableWidth)
    {
        return currentX > 0 && currentX + childWidth > availableWidth;
    }
}
