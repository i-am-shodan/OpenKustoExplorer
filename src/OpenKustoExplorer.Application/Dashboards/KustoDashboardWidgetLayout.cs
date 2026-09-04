namespace OpenKustoExplorer.Application.Dashboards;

/// <summary>
/// Defines one widget's snapped dashboard-grid placement.
/// </summary>
public sealed class KustoDashboardWidgetLayout
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardWidgetLayout"/> class.
    /// </summary>
    /// <param name="column">The zero-based grid column.</param>
    /// <param name="row">The zero-based grid row.</param>
    /// <param name="columnSpan">The positive width in grid units.</param>
    /// <param name="rowSpan">The positive height in grid units.</param>
    public KustoDashboardWidgetLayout(int column, int row, int columnSpan, int rowSpan)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfLessThan(columnSpan, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowSpan, 1);
        Column = column;
        Row = row;
        ColumnSpan = columnSpan;
        RowSpan = rowSpan;
    }

    /// <summary>
    /// Gets the zero-based grid column.
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// Gets the zero-based grid row.
    /// </summary>
    public int Row { get; }

    /// <summary>
    /// Gets the width in grid units.
    /// </summary>
    public int ColumnSpan { get; }

    /// <summary>
    /// Gets the height in grid units.
    /// </summary>
    public int RowSpan { get; }
}
