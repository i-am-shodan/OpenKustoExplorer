namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Describes one projected openCypher result column.
/// </summary>
public sealed class GraphQueryColumn
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQueryColumn"/> class.
    /// </summary>
    /// <param name="name">The projected alias or expression text.</param>
    /// <param name="kind">The projected value kind.</param>
    public GraphQueryColumn(string name, GraphQueryValueKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Kind = kind;
    }

    /// <summary>
    /// Gets the projected alias or expression text.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the projected value kind.
    /// </summary>
    public GraphQueryValueKind Kind { get; }
}
