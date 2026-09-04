namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Describes one bounded read-only openCypher query against an immutable graph snapshot.
/// </summary>
public sealed class GraphQueryRequest
{
    /// <summary>
    /// Gets the maximum accepted openCypher text length.
    /// </summary>
    public const int MaximumQueryTextLength = 32 * 1024;

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQueryRequest"/> class.
    /// </summary>
    /// <param name="snapshot">The immutable graph generation to query.</param>
    /// <param name="queryText">The complete read-only openCypher query.</param>
    /// <param name="maximumRowCount">The maximum projected row count.</param>
    /// <param name="maximumEntityCount">The maximum matched viewport entity count.</param>
    /// <param name="maximumRelationshipCount">The maximum matched viewport relationship count.</param>
    public GraphQueryRequest(
        GraphSnapshot snapshot,
        string queryText,
        int maximumRowCount = 200,
        int maximumEntityCount = 500,
        int maximumRelationshipCount = 2_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(queryText.Length, MaximumQueryTextLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRowCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRowCount, 1_000);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntityCount, 500);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelationshipCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRelationshipCount, 2_000);
        Snapshot = snapshot;
        QueryText = queryText.Trim();
        MaximumRowCount = maximumRowCount;
        MaximumEntityCount = maximumEntityCount;
        MaximumRelationshipCount = maximumRelationshipCount;
    }

    /// <summary>
    /// Gets the graph generation captured when execution begins.
    /// </summary>
    public GraphSnapshot Snapshot { get; }

    /// <summary>
    /// Gets the complete read-only openCypher text.
    /// </summary>
    public string QueryText { get; }

    /// <summary>
    /// Gets the maximum projected row count.
    /// </summary>
    public int MaximumRowCount { get; }

    /// <summary>
    /// Gets the maximum matched viewport entity count.
    /// </summary>
    public int MaximumEntityCount { get; }

    /// <summary>
    /// Gets the maximum matched viewport relationship count.
    /// </summary>
    public int MaximumRelationshipCount { get; }
}
