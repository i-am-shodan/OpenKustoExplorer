using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Executes a validated terminal graph export and streams nodes and edges to staged storage.
/// </summary>
public interface IKustoGraphQueryService
{
    /// <summary>
    /// Executes a graph export without applying the ordinary result-grid row limit.
    /// </summary>
    /// <param name="request">The original cluster and database query request.</param>
    /// <param name="plan">The validated graph-to-table export plan.</param>
    /// <param name="sink">The bounded-memory node and edge sink.</param>
    /// <param name="cancellationToken">A token that cancels query execution and staged ingestion.</param>
    /// <returns>Streamed row counts and measured duration.</returns>
    public Task<KustoGraphExportSummary> ExecuteGraphAsync(
        KustoQueryRequest request,
        KustoGraphQueryPlan plan,
        IKustoGraphExportSink sink,
        CancellationToken cancellationToken = default);
}
