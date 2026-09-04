using Avalonia;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Desktop.Graphs;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies export behavior for the interactive graph control.
/// </summary>
public sealed class KustoGraphControlTests
{
    /// <summary>
    /// Verifies a bent route becomes one smooth cubic Bezier with exact endpoints.
    /// </summary>
    [Fact]
    public void CurvedRouteUsesSingleBezier()
    {
        GraphLayoutPoint[] route =
        [
            new GraphLayoutPoint(0, 0),
            new GraphLayoutPoint(50, 80),
            new GraphLayoutPoint(100, 0),
        ];

        KustoGraphCurveSegment[] segments = KustoGraphCurveGeometry.CreateSegments(route);

        KustoGraphCurveSegment segment = Assert.Single(segments);
        Assert.Equal(route[0], segment.Start);
        Assert.Equal(route[^1], segment.End);
        Assert.True(KustoGraphCurveGeometry.GetPoint(segment, 0.5).Y > 0);
    }

    /// <summary>
    /// Verifies an unobstructed sampled route remains visually straight.
    /// </summary>
    [Fact]
    public void StraightRouteUsesSingleStraightSegment()
    {
        GraphLayoutPoint[] route =
        [
            new GraphLayoutPoint(10, 20),
            new GraphLayoutPoint(60, 45),
            new GraphLayoutPoint(110, 70),
        ];

        KustoGraphCurveSegment[] segments = KustoGraphCurveGeometry.CreateSegments(route);

        KustoGraphCurveSegment segment = Assert.Single(segments);
        Assert.Equal(10 + (100d / 3), segment.FirstControl.X, precision: 10);
        Assert.Equal(20 + (50d / 3), segment.FirstControl.Y, precision: 10);
        Assert.Equal(110 - (100d / 3), segment.SecondControl.X, precision: 10);
        Assert.Equal(70 - (50d / 3), segment.SecondControl.Y, precision: 10);
    }

    /// <summary>
    /// Verifies a clear diagonal edge stays straight while meeting both node boundaries.
    /// </summary>
    [Fact]
    public void StraightEdgeConnectsNearestNodeBoundaries()
    {
        DateTimeOffset observedAt = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        GraphEntityKey source = new(GraphEntityKind.User, "User", "alice@example.com");
        GraphEntityKey target = new(GraphEntityKind.Device, "Device", "device-42");
        GraphLayoutNode sourceNode = new(
            new GraphEntitySummary(source, "Alice", observedAt, observedAt, 1),
            new GraphLayoutPoint(100, 100),
            80,
            40,
            true);
        GraphLayoutNode targetNode = new(
            new GraphEntitySummary(target, "Device 42", observedAt, observedAt, 1),
            new GraphLayoutPoint(300, 160),
            100,
            60,
            false);
        GraphLayoutEdge edge = new(
            new GraphRelationshipKey(source, target, "AuthenticatedTo"),
            [
                new GraphLayoutPoint(140, 112),
                new GraphLayoutPoint(200, 130),
                new GraphLayoutPoint(250, 145),
            ]);

        KustoGraphCurveSegment[] segments = KustoGraphCurveGeometry.CreateSegments(
            edge,
            sourceNode,
            targetNode,
            new Dictionary<GraphEntityKey, Vector>());

        KustoGraphCurveSegment segment = Assert.Single(segments);
        Assert.Equal(new GraphLayoutPoint(140, 112), segment.Start);
        Assert.Equal(new GraphLayoutPoint(250, 145), segment.End);
        Assert.Equal(140 + (110d / 3), segment.FirstControl.X, precision: 10);
        Assert.Equal(112 + (33d / 3), segment.FirstControl.Y, precision: 10);
        Assert.Equal(250 - (110d / 3), segment.SecondControl.X, precision: 10);
        Assert.Equal(145 - (33d / 3), segment.SecondControl.Y, precision: 10);
    }

    /// <summary>
    /// Verifies independently dragged endpoints deform the complete smooth route without detaching it.
    /// </summary>
    [Fact]
    public void CurveSegmentsFollowDraggedNodePositions()
    {
        DateTimeOffset observedAt = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        GraphEntityKey source = new(GraphEntityKind.User, "User", "alice@example.com");
        GraphEntityKey target = new(GraphEntityKind.Device, "Device", "device-42");
        GraphLayoutNode sourceNode = new(
            new GraphEntitySummary(source, "Alice", observedAt, observedAt, 1),
            new GraphLayoutPoint(100, 100),
            80,
            40,
            true);
        GraphLayoutNode targetNode = new(
            new GraphEntitySummary(target, "Device 42", observedAt, observedAt, 1),
            new GraphLayoutPoint(300, 130),
            100,
            60,
            false);
        GraphLayoutEdge edge = new(
            new GraphRelationshipKey(source, target, "AuthenticatedTo"),
            [
                new GraphLayoutPoint(138, 114),
                new GraphLayoutPoint(200, 90),
                new GraphLayoutPoint(252, 108),
            ]);
        Dictionary<GraphEntityKey, Vector> offsets = new()
        {
            [source] = new Vector(10, -5),
            [target] = new Vector(-20, 30),
        };

        KustoGraphCurveSegment[] segments = KustoGraphCurveGeometry.CreateSegments(
            edge,
            sourceNode,
            targetNode,
            offsets);

        KustoGraphCurveSegment segment = Assert.Single(segments);
        Assert.Equal(new GraphLayoutPoint(150, 95), segment.Start);
        Assert.True(segment.FirstControl.X > segment.Start.X);
        Assert.True(segment.FirstControl.Y > segment.Start.Y);
        Assert.Equal(new GraphLayoutPoint(230, 160), segment.End);
        Assert.True(segment.SecondControl.X < segment.End.X);
        Assert.True(segment.SecondControl.Y < segment.End.Y);
    }

    /// <summary>
    /// Verifies vertically separated nodes connect through exact top and bottom side midpoints.
    /// </summary>
    [Fact]
    public void CurveSegmentsUseVerticalSideMidpoints()
    {
        DateTimeOffset observedAt = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        GraphEntityKey source = new(GraphEntityKind.User, "User", "alice@example.com");
        GraphEntityKey target = new(GraphEntityKind.Device, "Device", "device-42");
        GraphLayoutNode sourceNode = new(
            new GraphEntitySummary(source, "Alice", observedAt, observedAt, 1),
            new GraphLayoutPoint(100, 100),
            80,
            40,
            true);
        GraphLayoutNode targetNode = new(
            new GraphEntitySummary(target, "Device 42", observedAt, observedAt, 1),
            new GraphLayoutPoint(120, 300),
            100,
            60,
            false);
        GraphLayoutEdge edge = new(
            new GraphRelationshipKey(source, target, "AuthenticatedTo"),
            [
                new GraphLayoutPoint(115, 118),
                new GraphLayoutPoint(80, 200),
                new GraphLayoutPoint(145, 272),
            ]);

        KustoGraphCurveSegment[] segments = KustoGraphCurveGeometry.CreateSegments(
            edge,
            sourceNode,
            targetNode,
            new Dictionary<GraphEntityKey, Vector>());

        KustoGraphCurveSegment segment = Assert.Single(segments);
        Assert.Equal(new GraphLayoutPoint(100, 120), segment.Start);
        Assert.True(segment.FirstControl.X < segment.Start.X);
        Assert.True(segment.FirstControl.Y > segment.Start.Y);
        Assert.Equal(new GraphLayoutPoint(120, 270), segment.End);
        Assert.True(segment.SecondControl.X < segment.End.X);
        Assert.True(segment.SecondControl.Y < segment.End.Y);
    }

    /// <summary>
    /// Verifies relationship hit-testing follows the rendered cubic curve.
    /// </summary>
    [Fact]
    public void CurveDistanceTracksRenderedCubicSegments()
    {
        KustoGraphCurveSegment[] segments = KustoGraphCurveGeometry.CreateSegments(
        [
            new GraphLayoutPoint(0, 0),
            new GraphLayoutPoint(50, 100),
            new GraphLayoutPoint(100, 0),
        ]);
        GraphLayoutPoint pointOnCurve = KustoGraphCurveGeometry.GetPoint(segments[0], 0.5);

        double distanceSquared = KustoGraphCurveGeometry.GetDistanceSquared(pointOnCurve, segments);

        Assert.InRange(distanceSquared, 0, 0.000001);
    }

    /// <summary>
    /// Verifies that manual node positions are applied to nodes and interpolated across edge routes.
    /// </summary>
    [Fact]
    public void CreateExportLayoutAppliesManualNodeOffsetsToNodesAndRoutes()
    {
        DateTimeOffset observedAt = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        GraphEntityKey source = new(GraphEntityKind.User, "User", "alice@example.com");
        GraphEntityKey target = new(GraphEntityKind.Device, "Device", "device-42");
        GraphLayout layout = new(
            400,
            240,
            [
                new GraphLayoutNode(
                    new GraphEntitySummary(source, "Alice", observedAt, observedAt, 1),
                    new GraphLayoutPoint(100, 50),
                    80,
                    36,
                    true),
                new GraphLayoutNode(
                    new GraphEntitySummary(target, "Device 42", observedAt, observedAt, 1),
                    new GraphLayoutPoint(300, 150),
                    80,
                    36,
                    false),
            ],
            [
                new GraphLayoutEdge(
                    new GraphRelationshipKey(source, target, "AuthenticatedTo"),
                    [
                        new GraphLayoutPoint(100, 50),
                        new GraphLayoutPoint(200, 100),
                        new GraphLayoutPoint(300, 150),
                    ]),
            ],
            false);
        Dictionary<GraphEntityKey, Vector> offsets = new()
        {
            [source] = new Vector(10, -5),
            [target] = new Vector(-20, 30),
        };

        GraphLayout exportLayout = KustoGraphLayoutSnapshot.Create(layout, offsets);

        Assert.Equal(new GraphLayoutPoint(110, 45), exportLayout.Nodes[0].Center);
        Assert.Equal(new GraphLayoutPoint(280, 180), exportLayout.Nodes[1].Center);
        Assert.Equal(new GraphLayoutPoint(110, 45), exportLayout.Edges[0].Route[0]);
        Assert.Equal(new GraphLayoutPoint(195, 112.5), exportLayout.Edges[0].Route[1]);
        Assert.Equal(new GraphLayoutPoint(280, 180), exportLayout.Edges[0].Route[2]);
    }
}
