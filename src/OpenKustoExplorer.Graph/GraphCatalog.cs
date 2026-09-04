namespace OpenKustoExplorer.Graph;

/// <summary>
/// Contains all saved local graphs and the sole active selection.
/// </summary>
public sealed class GraphCatalog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphCatalog"/> class.
    /// </summary>
    /// <param name="activeGraphId">The active graph identifier.</param>
    /// <param name="graphs">The saved graphs.</param>
    public GraphCatalog(Guid activeGraphId, IEnumerable<GraphCatalogEntry> graphs)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(activeGraphId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(graphs);
        GraphCatalogEntry[] snapshot = graphs.ToArray();

        if (snapshot.Length == 0)
        {
            throw new ArgumentException("A graph catalog must contain at least one graph.", nameof(graphs));
        }

        if (snapshot.Select(graph => graph.GraphId).Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("A graph catalog cannot contain duplicate graph identifiers.", nameof(graphs));
        }

        GraphCatalogEntry activeGraph = snapshot.SingleOrDefault(graph => graph.GraphId == activeGraphId)
            ?? throw new ArgumentException("The active graph must exist in the catalog.", nameof(activeGraphId));
        ActiveGraphId = activeGraphId;
        ActiveGraph = activeGraph;
        Graphs = Array.AsReadOnly(snapshot);
    }

    /// <summary>
    /// Gets the sole active graph identifier.
    /// </summary>
    public Guid ActiveGraphId { get; }

    /// <summary>
    /// Gets the sole active graph.
    /// </summary>
    public GraphCatalogEntry ActiveGraph { get; }

    /// <summary>
    /// Gets all saved graphs in catalog order.
    /// </summary>
    public IReadOnlyList<GraphCatalogEntry> Graphs { get; }
}
