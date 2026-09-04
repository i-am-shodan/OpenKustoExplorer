using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Graphs;

/// <summary>
/// Describes one directed graph relationship and its sampled layout route.
/// </summary>
public sealed class GraphLayoutEdge
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphLayoutEdge"/> class.
    /// </summary>
    /// <param name="relationship">The directed graph relationship.</param>
    /// <param name="route">At least two ordered layout points from source to target.</param>
    public GraphLayoutEdge(
        GraphRelationshipKey relationship,
        IEnumerable<GraphLayoutPoint> route)
    {
        ArgumentNullException.ThrowIfNull(route);
        GraphLayoutPoint[] routeSnapshot = route.ToArray();

        if (routeSnapshot.Length < 2)
        {
            throw new ArgumentException("A graph edge route requires at least two points.", nameof(route));
        }

        Relationship = relationship;
        Route = Array.AsReadOnly(routeSnapshot);
    }

    /// <summary>
    /// Gets the directed graph relationship.
    /// </summary>
    public GraphRelationshipKey Relationship { get; }

    /// <summary>
    /// Gets ordered layout points from source to target.
    /// </summary>
    public IReadOnlyList<GraphLayoutPoint> Route { get; }
}
