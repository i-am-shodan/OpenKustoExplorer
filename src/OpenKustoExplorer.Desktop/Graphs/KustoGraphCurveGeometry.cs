using Avalonia;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Desktop.Graphs;

/// <summary>
/// Creates straight or single-curve geometry from routed graph points.
/// </summary>
internal static class KustoGraphCurveGeometry
{
    private const int HitTestSamplesPerSegment = 12;
    private const double StraightRouteTolerance = 0.75;

    /// <summary>
    /// Creates one cubic Bezier with manual node positions applied.
    /// </summary>
    /// <param name="edge">The routed graph edge.</param>
    /// <param name="sourceNode">The positioned source node.</param>
    /// <param name="targetNode">The positioned target node.</param>
    /// <param name="nodeOffsets">The current manual node offsets.</param>
    /// <returns>The adjusted smooth cubic segments.</returns>
    internal static KustoGraphCurveSegment[] CreateSegments(
        GraphLayoutEdge edge,
        GraphLayoutNode sourceNode,
        GraphLayoutNode targetNode,
        IReadOnlyDictionary<GraphEntityKey, Vector> nodeOffsets)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(sourceNode);
        ArgumentNullException.ThrowIfNull(targetNode);
        ArgumentNullException.ThrowIfNull(nodeOffsets);

        GraphLayoutPoint[] points = new GraphLayoutPoint[edge.Route.Count];
        Vector sourceOffset = GetNodeOffset(edge.Relationship.Source, nodeOffsets);
        Vector targetOffset = GetNodeOffset(edge.Relationship.Target, nodeOffsets);
        GraphLayoutPoint sourceCenter = GetNodeCenter(sourceNode, sourceOffset);
        GraphLayoutPoint targetCenter = GetNodeCenter(targetNode, targetOffset);
        for (int index = 0; index < points.Length; index++)
        {
            GraphLayoutPoint point = edge.Route[index];
            double progress = (double)index / (points.Length - 1);
            double offsetX = sourceOffset.X + ((targetOffset.X - sourceOffset.X) * progress);
            double offsetY = sourceOffset.Y + ((targetOffset.Y - sourceOffset.Y) * progress);
            points[index] = new GraphLayoutPoint(point.X + offsetX, point.Y + offsetY);
        }

        if (IsStraightRoute(points) && sourceCenter != targetCenter)
        {
            GraphLayoutPoint sourcePort = GetBoundaryIntersection(
                sourceNode,
                sourceCenter,
                targetCenter,
                1);
            GraphLayoutPoint targetPort = GetBoundaryIntersection(
                targetNode,
                targetCenter,
                sourceCenter,
                -1);
            return CreateSegments([sourcePort, targetPort]);
        }

        points[0] = GetSideMidpoint(sourceNode, sourceCenter, targetCenter, 1);
        points[^1] = GetSideMidpoint(targetNode, targetCenter, sourceCenter, -1);
        return CreateSegments(points);
    }

    /// <summary>
    /// Creates one cubic Bezier matching the endpoint tangents of a sampled route.
    /// </summary>
    /// <param name="points">The ordered routed points.</param>
    /// <returns>One cubic segment. Collinear samples produce a straight line.</returns>
    internal static KustoGraphCurveSegment[] CreateSegments(IReadOnlyList<GraphLayoutPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 2)
        {
            throw new ArgumentException("A graph curve requires at least two routed points.", nameof(points));
        }

        GraphLayoutPoint start = points[0];
        GraphLayoutPoint end = points[^1];
        double routeLength = GetRouteLength(points);
        Vector startTangent = GetStartTangent(points);
        Vector endTangent = GetEndTangent(points);
        double handleLength = routeLength / 3;
        GraphLayoutPoint firstControl = new(
            start.X + (startTangent.X * handleLength),
            start.Y + (startTangent.Y * handleLength));
        GraphLayoutPoint secondControl = new(
            end.X - (endTangent.X * handleLength),
            end.Y - (endTangent.Y * handleLength));
        return [new KustoGraphCurveSegment(start, firstControl, secondControl, end)];
    }

    /// <summary>
    /// Gets the shortest sampled distance from a point to a smooth graph curve.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <param name="segments">The cubic curve segments.</param>
    /// <returns>The squared distance to the curve.</returns>
    internal static double GetDistanceSquared(
        GraphLayoutPoint point,
        KustoGraphCurveSegment[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        double closestDistanceSquared = double.PositiveInfinity;

        foreach (KustoGraphCurveSegment segment in segments)
        {
            GraphLayoutPoint previous = segment.Start;
            for (int sample = 1; sample <= HitTestSamplesPerSegment; sample++)
            {
                GraphLayoutPoint current = GetPoint(
                    segment,
                    (double)sample / HitTestSamplesPerSegment);
                closestDistanceSquared = Math.Min(
                    closestDistanceSquared,
                    GetLineDistanceSquared(point, previous, current));
                previous = current;
            }
        }

        return closestDistanceSquared;
    }

    /// <summary>
    /// Gets a point along one cubic curve segment.
    /// </summary>
    /// <param name="segment">The cubic curve segment.</param>
    /// <param name="progress">Progress from zero at the start to one at the end.</param>
    /// <returns>The point on the curve.</returns>
    internal static GraphLayoutPoint GetPoint(KustoGraphCurveSegment segment, double progress)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(progress, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(progress, 1);
        double remaining = 1 - progress;
        double startWeight = remaining * remaining * remaining;
        double firstControlWeight = 3 * remaining * remaining * progress;
        double secondControlWeight = 3 * remaining * progress * progress;
        double endWeight = progress * progress * progress;
        double x = (segment.Start.X * startWeight)
            + (segment.FirstControl.X * firstControlWeight)
            + (segment.SecondControl.X * secondControlWeight)
            + (segment.End.X * endWeight);
        double y = (segment.Start.Y * startWeight)
            + (segment.FirstControl.Y * firstControlWeight)
            + (segment.SecondControl.Y * secondControlWeight)
            + (segment.End.Y * endWeight);
        return new GraphLayoutPoint(x, y);
    }

    private static double GetLineDistanceSquared(
        GraphLayoutPoint point,
        GraphLayoutPoint start,
        GraphLayoutPoint end)
    {
        double segmentX = end.X - start.X;
        double segmentY = end.Y - start.Y;
        double segmentLengthSquared = (segmentX * segmentX) + (segmentY * segmentY);
        double progress = segmentLengthSquared <= double.Epsilon
            ? 0
            : (((point.X - start.X) * segmentX) + ((point.Y - start.Y) * segmentY))
                / segmentLengthSquared;
        progress = Math.Clamp(progress, 0, 1);
        double closestX = start.X + (segmentX * progress);
        double closestY = start.Y + (segmentY * progress);
        double distanceX = point.X - closestX;
        double distanceY = point.Y - closestY;
        return (distanceX * distanceX) + (distanceY * distanceY);
    }

    private static GraphLayoutPoint GetBoundaryIntersection(
        GraphLayoutNode node,
        GraphLayoutPoint center,
        GraphLayoutPoint otherCenter,
        double defaultHorizontalDirection)
    {
        double deltaX = otherCenter.X - center.X;
        double deltaY = otherCenter.Y - center.Y;
        if (Math.Abs(deltaX) <= double.Epsilon && Math.Abs(deltaY) <= double.Epsilon)
        {
            deltaX = defaultHorizontalDirection;
        }

        double horizontalScale = Math.Abs(deltaX) <= double.Epsilon
            ? double.PositiveInfinity
            : (node.Width / 2) / Math.Abs(deltaX);
        double verticalScale = Math.Abs(deltaY) <= double.Epsilon
            ? double.PositiveInfinity
            : (node.Height / 2) / Math.Abs(deltaY);
        double scale = Math.Min(horizontalScale, verticalScale);
        return new GraphLayoutPoint(
            center.X + (deltaX * scale),
            center.Y + (deltaY * scale));
    }

    private static Vector GetEndTangent(IReadOnlyList<GraphLayoutPoint> points)
    {
        GraphLayoutPoint end = points[^1];
        for (int index = points.Count - 2; index >= 0; index--)
        {
            Vector tangent = GetUnitVector(points[index], end);
            if (tangent != default)
            {
                return tangent;
            }
        }

        return default;
    }

    private static double GetRouteLength(IReadOnlyList<GraphLayoutPoint> points)
    {
        double length = 0;
        for (int index = 1; index < points.Count; index++)
        {
            double deltaX = points[index].X - points[index - 1].X;
            double deltaY = points[index].Y - points[index - 1].Y;
            length += Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        }

        return length;
    }

    private static Vector GetStartTangent(IReadOnlyList<GraphLayoutPoint> points)
    {
        GraphLayoutPoint start = points[0];
        for (int index = 1; index < points.Count; index++)
        {
            Vector tangent = GetUnitVector(start, points[index]);
            if (tangent != default)
            {
                return tangent;
            }
        }

        return default;
    }

    private static Vector GetUnitVector(GraphLayoutPoint start, GraphLayoutPoint end)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        return length <= double.Epsilon
            ? default
            : new Vector(deltaX / length, deltaY / length);
    }

    private static bool IsStraightRoute(GraphLayoutPoint[] points)
    {
        GraphLayoutPoint start = points[0];
        GraphLayoutPoint end = points[^1];
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        if ((deltaX * deltaX) + (deltaY * deltaY) <= double.Epsilon)
        {
            return false;
        }

        double toleranceSquared = StraightRouteTolerance * StraightRouteTolerance;
        for (int index = 1; index < points.Length - 1; index++)
        {
            if (GetLineDistanceSquared(points[index], start, end) > toleranceSquared)
            {
                return false;
            }
        }

        return true;
    }

    private static GraphLayoutPoint GetNodeCenter(GraphLayoutNode node, Vector nodeOffset)
    {
        return new GraphLayoutPoint(
            node.Center.X + nodeOffset.X,
            node.Center.Y + nodeOffset.Y);
    }

    private static Vector GetNodeOffset(
        GraphEntityKey entity,
        IReadOnlyDictionary<GraphEntityKey, Vector> nodeOffsets)
    {
        return nodeOffsets.TryGetValue(entity, out Vector nodeOffset) ? nodeOffset : default;
    }

    private static GraphLayoutPoint GetSideMidpoint(
        GraphLayoutNode node,
        GraphLayoutPoint center,
        GraphLayoutPoint otherCenter,
        double defaultHorizontalDirection)
    {
        double deltaX = otherCenter.X - center.X;
        double deltaY = otherCenter.Y - center.Y;
        GraphLayoutPoint point;

        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            double horizontalDirection = deltaX == 0 ? defaultHorizontalDirection : Math.Sign(deltaX);
            point = new GraphLayoutPoint(
                center.X + (horizontalDirection * node.Width / 2),
                center.Y);
        }
        else
        {
            double verticalDirection = Math.Sign(deltaY);
            point = new GraphLayoutPoint(
                center.X,
                center.Y + (verticalDirection * node.Height / 2));
        }

        return point;
    }
}
