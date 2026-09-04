namespace OpenKustoExplorer.Graph;

/// <summary>
/// Describes connectivity and the bounded union of shortest routes between two graph entities.
/// </summary>
public sealed class GraphRouteResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphRouteResult"/> class.
    /// </summary>
    /// <param name="start">The route start entity.</param>
    /// <param name="end">The route end entity.</param>
    /// <param name="shortestHopCount">The shortest relationship count, or <see langword="null"/> when disconnected.</param>
    /// <param name="viewport">The bounded route viewport.</param>
    public GraphRouteResult(
        GraphEntityKey start,
        GraphEntityKey end,
        int? shortestHopCount,
        GraphViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentOutOfRangeException.ThrowIfNegative(shortestHopCount ?? 0);
        Start = start;
        End = end;
        ShortestHopCount = shortestHopCount;
        Viewport = viewport;
    }

    /// <summary>
    /// Gets the route end entity.
    /// </summary>
    public GraphEntityKey End { get; }

    /// <summary>
    /// Gets a value indicating whether the entities are connected.
    /// </summary>
    public bool IsConnected => ShortestHopCount is not null;

    /// <summary>
    /// Gets the number of relationships in a shortest route, or <see langword="null"/> when disconnected.
    /// </summary>
    public int? ShortestHopCount { get; }

    /// <summary>
    /// Gets the route start entity.
    /// </summary>
    public GraphEntityKey Start { get; }

    /// <summary>
    /// Gets the bounded union of shortest routes, or the two endpoints when disconnected.
    /// </summary>
    public GraphViewport Viewport { get; }
}
