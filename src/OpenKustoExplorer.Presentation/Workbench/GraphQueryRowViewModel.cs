using OpenKustoExplorer.Graph.Query;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one typed openCypher result row.
/// </summary>
public sealed class GraphQueryRowViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQueryRowViewModel"/> class.
    /// </summary>
    /// <param name="row">The immutable query row.</param>
    /// <param name="columnWidths">Column widths in projection order.</param>
    /// <param name="selectAction">Selects graph identities projected by a cell.</param>
    public GraphQueryRowViewModel(
        GraphQueryRow row,
        IReadOnlyList<double> columnWidths,
        Action<GraphQueryCellViewModel> selectAction)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(columnWidths);
        ArgumentNullException.ThrowIfNull(selectAction);

        if (row.Values.Count != columnWidths.Count)
        {
            throw new ArgumentException("Graph query column widths must match the row value count.", nameof(columnWidths));
        }

        Cells = Array.AsReadOnly(row.Values
            .Select((value, index) => new GraphQueryCellViewModel(
                value,
                columnWidths[index],
                selectAction))
            .ToArray());
    }

    /// <summary>
    /// Gets typed cells in projection order.
    /// </summary>
    public IReadOnlyList<GraphQueryCellViewModel> Cells { get; }
}
