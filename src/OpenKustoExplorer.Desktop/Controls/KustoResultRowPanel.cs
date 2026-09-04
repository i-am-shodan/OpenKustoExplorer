using Avalonia;
using Avalonia.Controls;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Desktop.Controls;

/// <summary>
/// Keeps result columns aligned at their content-fit widths and proportionally fills spare row width.
/// </summary>
public sealed class KustoResultRowPanel : Panel
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double measuredWidth = 0;
        double measuredHeight = 0;

        foreach (Control child in Children.Where(child => child.IsVisible))
        {
            double? columnWidth = GetColumnWidth(child);
            child.Measure(columnWidth is double width
                ? new Size(width, availableSize.Height)
                : availableSize);
            measuredWidth += columnWidth ?? child.DesiredSize.Width;
            measuredHeight = Math.Max(measuredHeight, child.DesiredSize.Height);
        }

        return new Size(measuredWidth, measuredHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Control[] visibleChildren = Children.Where(child => child.IsVisible).ToArray();
        double minimumWidth = visibleChildren.Sum(child => GetColumnWidth(child) ?? child.DesiredSize.Width);
        double arrangedWidth = Math.Max(minimumWidth, finalSize.Width);
        double widthScale = minimumWidth > 0 ? arrangedWidth / minimumWidth : 1;
        double currentX = 0;

        for (int childIndex = 0; childIndex < visibleChildren.Length; childIndex++)
        {
            Control child = visibleChildren[childIndex];
            double columnWidth = GetColumnWidth(child) ?? child.DesiredSize.Width;
            double childWidth = childIndex == visibleChildren.Length - 1
                ? arrangedWidth - currentX
                : columnWidth * widthScale;
            child.Arrange(new Rect(currentX, 0, childWidth, finalSize.Height));
            currentX += childWidth;
        }

        return finalSize;
    }

    private static double? GetColumnWidth(Control child)
    {
        return child.DataContext switch
        {
            KustoResultColumnViewModel column => column.DisplayWidth,
            KustoResultCellViewModel cell => cell.DisplayWidth,
            _ => null,
        };
    }
}
