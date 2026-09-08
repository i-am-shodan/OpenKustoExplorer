using CommunityToolkit.Mvvm.ComponentModel;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one row of display cells in a Kusto result table.
/// </summary>
public sealed class KustoResultRowViewModel : ObservableObject
{
    private string backgroundHex = "#00000000";

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultRowViewModel"/> class.
    /// </summary>
    /// <param name="row">The immutable result row.</param>
    /// <param name="rowIndex">The zero-based materialized row index.</param>
    /// <param name="columns">The result columns in server order.</param>
    /// <param name="columnWidths">Optional content-fitted widths in server column order.</param>
    public KustoResultRowViewModel(
        KustoResultRow row,
        int rowIndex,
        IReadOnlyList<KustoResultColumn> columns,
        IReadOnlyList<double>? columnWidths = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentNullException.ThrowIfNull(columns);

        if (columnWidths is not null && columnWidths.Count != columns.Count)
        {
            throw new ArgumentException("Column widths must match the result column count.", nameof(columnWidths));
        }

        RowIndex = rowIndex;
        KustoResultCellViewModel[] cells = row.Values
            .Select((value, columnIndex) => new KustoResultCellViewModel(
                this,
                columnIndex,
                columns[columnIndex].Name,
                columns[columnIndex].TypeName,
                row.ResultValues[columnIndex],
                columnWidths?[columnIndex] ?? 150))
            .ToArray();
        Cells = Array.AsReadOnly(cells);
    }

    /// <summary>
    /// Gets the zero-based materialized row index.
    /// </summary>
    public int RowIndex { get; }

    /// <summary>
    /// Gets the display cells in column order.
    /// </summary>
    public IReadOnlyList<KustoResultCellViewModel> Cells { get; }

    /// <summary>
    /// Gets the complete row context exposed to assistive technology.
    /// </summary>
    public string AutomationText => $"Row {RowIndex + 1}: {string.Join("; ", Cells.Select(cell => $"{cell.ColumnName}: {cell.AutomationValueText}"))}";

    /// <summary>
    /// Gets the alternating or conditional row background color.
    /// </summary>
    public string BackgroundHex
    {
        get => backgroundHex;
        private set => SetProperty(ref backgroundHex, value);
    }

    /// <summary>
    /// Replaces the row background color.
    /// </summary>
    /// <param name="colorHex">The ARGB or RGB color text.</param>
    internal void SetBackground(string colorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
        BackgroundHex = colorHex;
    }
}
