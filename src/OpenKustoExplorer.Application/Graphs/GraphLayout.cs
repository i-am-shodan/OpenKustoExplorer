using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Graphs;

/// <summary>
/// Contains bounded graph nodes and routed edges in normalized render coordinates.
/// </summary>
public sealed class GraphLayout
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphLayout"/> class.
    /// </summary>
    /// <param name="width">The complete layout width.</param>
    /// <param name="height">The complete layout height.</param>
    /// <param name="nodes">The positioned graph nodes.</param>
    /// <param name="edges">The routed graph edges.</param>
    /// <param name="isTruncated">Whether the source viewport omitted neighboring data.</param>
    public GraphLayout(
        double width,
        double height,
        IEnumerable<GraphLayoutNode> nodes,
        IEnumerable<GraphLayoutEdge> edges,
        bool isTruncated)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        GraphLayoutNode[] nodeSnapshot = nodes.ToArray();
        GraphLayoutEdge[] edgeSnapshot = edges.ToArray();
        HashSet<GraphEntityKey> nodeKeys = nodeSnapshot
            .Select(node => node.Entity.Entity)
            .ToHashSet();

        if (nodeKeys.Count != nodeSnapshot.Length)
        {
            throw new ArgumentException("A graph layout cannot contain duplicate nodes.", nameof(nodes));
        }

        if (edgeSnapshot.Any(edge => !nodeKeys.Contains(edge.Relationship.Source)
            || !nodeKeys.Contains(edge.Relationship.Target)))
        {
            throw new ArgumentException("Every graph layout edge endpoint must be positioned.", nameof(edges));
        }

        Width = width;
        Height = height;
        Nodes = Array.AsReadOnly(nodeSnapshot);
        Edges = Array.AsReadOnly(edgeSnapshot);
        IsTruncated = isTruncated;
    }

    /// <summary>
    /// Gets the routed graph edges.
    /// </summary>
    public IReadOnlyList<GraphLayoutEdge> Edges { get; }

    /// <summary>
    /// Gets the complete layout height.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Gets a value indicating whether no nodes are available to render.
    /// </summary>
    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>
    /// Gets a value indicating whether the source viewport omitted neighboring data.
    /// </summary>
    public bool IsTruncated { get; }

    /// <summary>
    /// Gets the positioned graph nodes.
    /// </summary>
    public IReadOnlyList<GraphLayoutNode> Nodes { get; }

    /// <summary>
    /// Gets the complete layout width.
    /// </summary>
    public double Width { get; }
}
