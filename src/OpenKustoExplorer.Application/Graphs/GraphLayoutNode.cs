using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Graphs;

/// <summary>
/// Places one graph entity in layout coordinates.
/// </summary>
public sealed class GraphLayoutNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphLayoutNode"/> class.
    /// </summary>
    /// <param name="entity">The graph entity represented by this node.</param>
    /// <param name="center">The node center in layout coordinates.</param>
    /// <param name="width">The node width.</param>
    /// <param name="height">The node height.</param>
    /// <param name="isCenter">Whether this node is the viewport center.</param>
    public GraphLayoutNode(
        GraphEntitySummary entity,
        GraphLayoutPoint center,
        double width,
        double height,
        bool isCenter)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Entity = entity;
        Center = center;
        Width = width;
        Height = height;
        IsCenter = isCenter;
    }

    /// <summary>
    /// Gets the node center in layout coordinates.
    /// </summary>
    public GraphLayoutPoint Center { get; }

    /// <summary>
    /// Gets the graph entity represented by this node.
    /// </summary>
    public GraphEntitySummary Entity { get; }

    /// <summary>
    /// Gets the node height.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Gets a value indicating whether this node is the viewport center.
    /// </summary>
    public bool IsCenter { get; }

    /// <summary>
    /// Gets the node width.
    /// </summary>
    public double Width { get; }
}
