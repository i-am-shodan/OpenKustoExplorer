using Microsoft.Msagl.Core;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Layout.ProximityOverlapRemoval.MinimumSpanningTree;
using Microsoft.Msagl.Layout.MDS;
using Microsoft.Msagl.Miscellaneous;
using Microsoft.Msagl.Routing;
using OpenKustoExplorer.Application.Diagnostics;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Graph;
using MsaglPoint = Microsoft.Msagl.Core.Geometry.Point;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Uses MSAGL to calculate bounded node positions and routed edge geometry.
/// </summary>
public sealed class MsaglGraphLayoutService : IGraphLayoutService
{
    private const double LayoutPadding = 48;
    private const double MaximumNodeWidth = 240;
    private const double MaximumReadableAspectRatio = 2.5;
    private const double MinimumReadableAspectRatio = 0.75;
    private const double MinimumNodeWidth = 128;
    private const double NodeHeight = 52;
    private const double TargetAspectRatio = 1.6;

    /// <inheritdoc />
    public Task<GraphLayout> LayoutAsync(
        GraphViewport viewport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => CreateLayout(viewport, cancellationToken), cancellationToken);
    }

    private static GraphLayout CreateLayout(
        GraphViewport viewport,
        CancellationToken cancellationToken)
    {
        using KustoPerformanceTrace.OperationScope measurement = KustoPerformanceTrace.Measure(
            "graph.layout.create",
            viewport.Entities.Count);
        if (viewport.IsEmpty)
        {
            return new GraphLayout(0, 0, [], [], viewport.IsTruncated);
        }

        GeometryGraph geometryGraph = new();
        Dictionary<GraphEntityKey, Node> geometryNodes = [];

        foreach (GraphEntitySummary entity in viewport.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double nodeWidth = Math.Clamp(
                MinimumNodeWidth + (entity.DisplayLabel.Length * 4.5),
                MinimumNodeWidth,
                MaximumNodeWidth);
            Node geometryNode = new(CurveFactory.CreateRectangleWithRoundedCorners(
                nodeWidth,
                NodeHeight,
                7,
                7,
                new MsaglPoint(0, 0)));
            geometryGraph.Nodes.Add(geometryNode);
            geometryNodes.Add(entity.Entity, geometryNode);
        }

        List<(GraphRelationshipKey Relationship, Edge Geometry)> geometryEdges = [];
        foreach (GraphRelationshipKey relationship in viewport.Relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Edge geometryEdge = new(
                geometryNodes[relationship.Source],
                geometryNodes[relationship.Target]);
            geometryGraph.Edges.Add(geometryEdge);
            geometryEdges.Add((relationship, geometryEdge));
        }

        MdsLayoutSettings topologySettings = new()
        {
            AdjustScale = true,
            IterationsWithMajorization = 30,
            NodeSeparation = 42,
            PackingAspectRatio = TargetAspectRatio,
            RemoveOverlaps = true,
        };
        CancelToken layoutCancelToken = new();
        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
            () => layoutCancelToken.Canceled = true);
        topologySettings.EdgeRoutingSettings.EdgeRoutingMode = Microsoft.Msagl.Core.Routing.EdgeRoutingMode.None;
        LayoutHelpers.CalculateLayout(geometryGraph, topologySettings, layoutCancelToken);
        cancellationToken.ThrowIfCancellationRequested();
        OrientElongatedGraph(geometryGraph);
        GTreeOverlapRemoval.RemoveOverlaps(geometryGraph.Nodes.ToArray(), topologySettings.NodeSeparation);
        SplineRouter router = new(geometryGraph, 7, 7, Math.PI / 6);
        router.Run(layoutCancelToken);
        geometryGraph.UpdateBoundingBox();
        cancellationToken.ThrowIfCancellationRequested();
        Microsoft.Msagl.Core.Geometry.Rectangle bounds = geometryGraph.BoundingBox;
        Dictionary<GraphEntityKey, GraphLayoutNode> layoutNodes = [];

        foreach (GraphEntitySummary entity in viewport.Entities)
        {
            Node geometryNode = geometryNodes[entity.Entity];
            GraphLayoutPoint center = Normalize(geometryNode.Center, bounds);
            layoutNodes.Add(
                entity.Entity,
                new GraphLayoutNode(
                    entity,
                    center,
                    geometryNode.BoundingBox.Width,
                    geometryNode.BoundingBox.Height,
                    viewport.Center is GraphEntityKey centerEntity && centerEntity == entity.Entity));
        }

        List<GraphLayoutEdge> layoutEdges = [];
        foreach ((GraphRelationshipKey relationship, Edge geometryEdge) in geometryEdges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<GraphLayoutPoint> route = CreateRoute(
                geometryEdge,
                bounds,
                layoutNodes[relationship.Source],
                layoutNodes[relationship.Target]);
            layoutEdges.Add(new GraphLayoutEdge(relationship, route));
        }

        return new GraphLayout(
            bounds.Width + (LayoutPadding * 2),
            bounds.Height + (LayoutPadding * 2),
            layoutNodes.Values,
            layoutEdges,
            viewport.IsTruncated);
    }

    private static GraphLayoutPoint[] CreateRoute(
        Edge geometryEdge,
        Microsoft.Msagl.Core.Geometry.Rectangle bounds,
        GraphLayoutNode source,
        GraphLayoutNode target)
    {
        ICurve? curve = geometryEdge.Curve;

        if (curve is null || curve.Length <= 0)
        {
            return [source.Center, target.Center];
        }

        int segmentCount = Math.Clamp((int)Math.Ceiling(curve.Length / 28), 2, 24);
        GraphLayoutPoint[] route = new GraphLayoutPoint[segmentCount + 1];

        for (int index = 0; index <= segmentCount; index++)
        {
            double parameter = curve.ParStart
                + ((curve.ParEnd - curve.ParStart) * index / segmentCount);
            route[index] = Normalize(curve[parameter], bounds);
        }

        return route;
    }

    private static GraphLayoutPoint Normalize(
        MsaglPoint point,
        Microsoft.Msagl.Core.Geometry.Rectangle bounds)
    {
        return new GraphLayoutPoint(
            point.X - bounds.Left + LayoutPadding,
            bounds.Top - point.Y + LayoutPadding);
    }

    private static void OrientElongatedGraph(GeometryGraph geometryGraph)
    {
        if (geometryGraph.Nodes.Count < 2)
        {
            return;
        }

        Microsoft.Msagl.Core.Geometry.Rectangle bounds = geometryGraph.BoundingBox;
        double aspectRatio = bounds.Width / Math.Max(bounds.Height, double.Epsilon);
        bool isElongated = aspectRatio < MinimumReadableAspectRatio
            || aspectRatio > MaximumReadableAspectRatio;

        if (!isElongated)
        {
            return;
        }

        MsaglPoint[] centers = geometryGraph.Nodes.Select(node => node.Center).ToArray();
        double averageX = centers.Average(point => point.X);
        double averageY = centers.Average(point => point.Y);
        double covarianceXy = centers.Sum(point => (point.X - averageX) * (point.Y - averageY));
        double varianceX = centers.Sum(point => Math.Pow(point.X - averageX, 2));
        double varianceY = centers.Sum(point => Math.Pow(point.Y - averageY, 2));

        double majorAxisAngle = 0.5 * Math.Atan2(2 * covarianceXy, varianceX - varianceY);
        double targetAngle = Math.Atan(1 / TargetAspectRatio);
        double rotation = targetAngle - majorAxisAngle;
        double cosine = Math.Cos(rotation);
        double sine = Math.Sin(rotation);

        foreach (Node node in geometryGraph.Nodes)
        {
            double deltaX = node.Center.X - averageX;
            double deltaY = node.Center.Y - averageY;
            node.Center = new MsaglPoint(
                averageX + (deltaX * cosine) - (deltaY * sine),
                averageY + (deltaX * sine) + (deltaY * cosine));
        }

        geometryGraph.UpdateBoundingBox();
    }
}
