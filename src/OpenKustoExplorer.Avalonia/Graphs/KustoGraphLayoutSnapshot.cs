using Avalonia;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Desktop.Graphs;

/// <summary>
/// Applies interactive node positions to immutable graph layouts for export.
/// </summary>
internal static class KustoGraphLayoutSnapshot
{
    /// <summary>
    /// Creates a graph layout with manual node offsets applied to nodes and routed edges.
    /// </summary>
    /// <param name="graphLayout">The automatic graph layout.</param>
    /// <param name="manualNodeOffsets">The current manual node offsets.</param>
    /// <returns>An immutable adjusted layout.</returns>
    internal static GraphLayout Create(
        GraphLayout graphLayout,
        Dictionary<GraphEntityKey, Vector> manualNodeOffsets)
    {
        ArgumentNullException.ThrowIfNull(graphLayout);
        ArgumentNullException.ThrowIfNull(manualNodeOffsets);

        GraphLayoutNode[] nodes = graphLayout.Nodes
            .Select(node =>
            {
                Vector nodeOffset = GetNodeOffset(node.Entity.Entity, manualNodeOffsets);
                return new GraphLayoutNode(
                    node.Entity,
                    new GraphLayoutPoint(
                        node.Center.X + nodeOffset.X,
                        node.Center.Y + nodeOffset.Y),
                    node.Width,
                    node.Height,
                    node.IsCenter);
            })
            .ToArray();
        GraphLayoutEdge[] edges = graphLayout.Edges
            .Select(edge => new GraphLayoutEdge(
                edge.Relationship,
                edge.Route.Select((_, index) => GetRoutePoint(edge, index, manualNodeOffsets))))
            .ToArray();

        return new GraphLayout(
            graphLayout.Width,
            graphLayout.Height,
            nodes,
            edges,
            graphLayout.IsTruncated);
    }

    private static Vector GetNodeOffset(
        GraphEntityKey entity,
        Dictionary<GraphEntityKey, Vector> manualNodeOffsets)
    {
        return manualNodeOffsets.TryGetValue(entity, out Vector nodeOffset) ? nodeOffset : default;
    }

    private static GraphLayoutPoint GetRoutePoint(
        GraphLayoutEdge edge,
        int index,
        Dictionary<GraphEntityKey, Vector> manualNodeOffsets)
    {
        GraphLayoutPoint point = edge.Route[index];
        double progress = (double)index / (edge.Route.Count - 1);
        Vector sourceOffset = GetNodeOffset(edge.Relationship.Source, manualNodeOffsets);
        Vector targetOffset = GetNodeOffset(edge.Relationship.Target, manualNodeOffsets);
        return new GraphLayoutPoint(
            point.X + sourceOffset.X + ((targetOffset.X - sourceOffset.X) * progress),
            point.Y + sourceOffset.Y + ((targetOffset.Y - sourceOffset.Y) * progress));
    }
}
