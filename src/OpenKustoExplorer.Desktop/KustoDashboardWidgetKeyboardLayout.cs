using Avalonia.Input;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Applies bounded keyboard movement and resizing to dashboard widgets.
/// </summary>
internal static class KustoDashboardWidgetKeyboardLayout
{
    /// <summary>Gets the dashboard grid column count.</summary>
    internal const int ColumnCount = 48;

    /// <summary>Gets the maximum dashboard grid row count.</summary>
    internal const int MaximumRowCount = 80;

    /// <summary>Gets the minimum widget column span.</summary>
    internal const int MinimumColumnSpan = 8;

    /// <summary>Gets the minimum widget row span.</summary>
    internal const int MinimumRowSpan = 6;

    /// <summary>
    /// Applies one bounded keyboard move or resize step to a dashboard widget.
    /// </summary>
    /// <param name="widget">The widget to adjust.</param>
    /// <param name="key">The requested arrow direction.</param>
    /// <param name="isResize">Whether to resize instead of move.</param>
    /// <returns><see langword="true"/> when the key represents a layout action.</returns>
    internal static bool TryAdjust(
        KustoDashboardWidgetViewModel widget,
        Key key,
        bool isResize)
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (key is not (Key.Left or Key.Right or Key.Up or Key.Down))
        {
            return false;
        }

        int column = widget.Column;
        int row = widget.Row;
        int columnSpan = widget.ColumnSpan;
        int rowSpan = widget.RowSpan;
        if (isResize)
        {
            columnSpan = key switch
            {
                Key.Left => Math.Max(MinimumColumnSpan, columnSpan - 1),
                Key.Right => Math.Min(ColumnCount - column, columnSpan + 1),
                _ => columnSpan,
            };
            rowSpan = key switch
            {
                Key.Up => Math.Max(MinimumRowSpan, rowSpan - 1),
                Key.Down => Math.Min(MaximumRowCount - row, rowSpan + 1),
                _ => rowSpan,
            };
        }
        else
        {
            column = key switch
            {
                Key.Left => Math.Max(0, column - 1),
                Key.Right => Math.Min(ColumnCount - columnSpan, column + 1),
                _ => column,
            };
            row = key switch
            {
                Key.Up => Math.Max(0, row - 1),
                Key.Down => Math.Min(MaximumRowCount - rowSpan, row + 1),
                _ => row,
            };
        }

        widget.PreviewLayout(column, row, columnSpan, rowSpan);
        widget.CommitLayout();
        return true;
    }
}
