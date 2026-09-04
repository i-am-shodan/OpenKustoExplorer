namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Executes bounded read-only openCypher against immutable named graph snapshots.
/// </summary>
public interface IGraphQueryService
{
    /// <summary>
    /// Gets bounded labels, relationship types, and properties for one graph snapshot.
    /// </summary>
    /// <param name="snapshot">The graph generation to describe.</param>
    /// <param name="cancellationToken">A token that cancels schema discovery.</param>
    /// <returns>The bounded graph query schema.</returns>
    public Task<GraphQuerySchema> GetSchemaAsync(
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses, validates, and executes one bounded read-only openCypher query.
    /// </summary>
    /// <param name="request">The pinned query text and result bounds.</param>
    /// <param name="cancellationToken">A token that cancels query execution.</param>
    /// <returns>The projected rows, matched viewport, and diagnostics.</returns>
    public Task<GraphQueryResult> ExecuteOpenCypherAsync(
        GraphQueryRequest request,
        CancellationToken cancellationToken = default);
}
