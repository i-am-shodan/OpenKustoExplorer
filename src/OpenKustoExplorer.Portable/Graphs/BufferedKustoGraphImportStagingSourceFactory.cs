using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Portable.Graphs;

/// <summary>
/// Creates bounded in-memory graph staging sources for hosts without local database access.
/// </summary>
public sealed class BufferedKustoGraphImportStagingSourceFactory : IKustoGraphImportStagingSourceFactory
{
    /// <inheritdoc />
    public IKustoGraphImportStagingSource Create(
        KustoGraphQueryPlan plan,
        KustoQueryRequest query,
        Guid ingestionId)
    {
        return new BufferedKustoGraphImportStagingSource(plan, query, ingestionId);
    }
}
