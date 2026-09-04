using OpenKustoExplorer.Graph.Query;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one openCypher projection column with a stable table width.
/// </summary>
public sealed class GraphQueryColumnViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQueryColumnViewModel"/> class.
    /// </summary>
    /// <param name="column">The immutable projected column.</param>
    /// <param name="displayWidth">The shared header and cell width.</param>
    public GraphQueryColumnViewModel(GraphQueryColumn column, double displayWidth)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(displayWidth);
        Name = column.Name;
        Kind = column.Kind;
        DisplayWidth = displayWidth;
    }

    /// <summary>
    /// Gets the projection column name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the projected value kind.
    /// </summary>
    public GraphQueryValueKind Kind { get; }

    /// <summary>
    /// Gets the shared header and cell width.
    /// </summary>
    public double DisplayWidth { get; }
}
