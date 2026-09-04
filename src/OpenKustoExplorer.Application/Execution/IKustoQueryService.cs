namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Executes KQL requests against a Kusto cluster.
/// </summary>
public interface IKustoQueryService
{
    /// <summary>
    /// Executes one query and materializes its tabular results.
    /// </summary>
    /// <param name="request">The immutable query request.</param>
    /// <param name="cancellationToken">A token that cancels authentication or execution.</param>
    /// <returns>The materialized result tables and execution duration.</returns>
    public Task<KustoQueryResult> ExecuteAsync(
        KustoQueryRequest request,
        CancellationToken cancellationToken = default);
}
