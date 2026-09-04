namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Contains one projected openCypher result tuple.
/// </summary>
public sealed class GraphQueryRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQueryRow"/> class.
    /// </summary>
    /// <param name="values">The projected values in column order.</param>
    public GraphQueryRow(IEnumerable<GraphQueryValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = Array.AsReadOnly(values.ToArray());
    }

    /// <summary>
    /// Gets projected values in column order.
    /// </summary>
    public IReadOnlyList<GraphQueryValue> Values { get; }
}
