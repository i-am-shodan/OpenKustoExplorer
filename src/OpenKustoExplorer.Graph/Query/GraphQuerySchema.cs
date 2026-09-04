namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Describes bounded labels, relationship types, and properties available in one graph snapshot.
/// </summary>
public sealed class GraphQuerySchema
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQuerySchema"/> class.
    /// </summary>
    /// <param name="snapshot">The graph snapshot described by this schema.</param>
    /// <param name="nodeLabels">The available node labels.</param>
    /// <param name="relationshipTypes">The available relationship types.</param>
    public GraphQuerySchema(
        GraphSnapshot snapshot,
        IEnumerable<GraphQuerySchemaEntry> nodeLabels,
        IEnumerable<GraphQuerySchemaEntry> relationshipTypes)
    {
        ArgumentNullException.ThrowIfNull(nodeLabels);
        ArgumentNullException.ThrowIfNull(relationshipTypes);
        Snapshot = snapshot;
        NodeLabels = Array.AsReadOnly(nodeLabels.ToArray());
        RelationshipTypes = Array.AsReadOnly(relationshipTypes.ToArray());
    }

    /// <summary>
    /// Gets the graph snapshot described by this schema.
    /// </summary>
    public GraphSnapshot Snapshot { get; }

    /// <summary>
    /// Gets available node labels.
    /// </summary>
    public IReadOnlyList<GraphQuerySchemaEntry> NodeLabels { get; }

    /// <summary>
    /// Gets available relationship types.
    /// </summary>
    public IReadOnlyList<GraphQuerySchemaEntry> RelationshipTypes { get; }
}
