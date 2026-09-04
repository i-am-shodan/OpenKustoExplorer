namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Executes a validated Kusto graph export and atomically imports it into the investigation graph.
/// </summary>
public interface IKustoGraphIngestionService
{
    /// <summary>
    /// Executes, validates, stages, and imports one graph query.
    /// </summary>
    /// <param name="request">The graph query, provenance, and import mode.</param>
    /// <param name="cancellationToken">A token that cancels execution and prevents an uncommitted import.</param>
    /// <returns>The export and durable import summaries.</returns>
    public Task<KustoGraphIngestionResult> ExecuteAsync(
        KustoGraphIngestionRequest request,
        CancellationToken cancellationToken = default);
}
