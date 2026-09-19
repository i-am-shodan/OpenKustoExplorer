using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Creates host-appropriate graph import staging sources.
/// </summary>
public interface IKustoGraphImportStagingSourceFactory
{
    /// <summary>
    /// Creates staging for one graph export.
    /// </summary>
    /// <param name="plan">The validated graph export plan.</param>
    /// <param name="query">The selected query and source database.</param>
    /// <param name="ingestionId">The identifier reserved for the ingestion.</param>
    /// <returns>A new staging source.</returns>
    public IKustoGraphImportStagingSource Create(
        KustoGraphQueryPlan plan,
        KustoQueryRequest query,
        Guid ingestionId);
}
