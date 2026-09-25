using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Executes graph ingestion through shared orchestration and disk-backed staging.
/// </summary>
public sealed class KustoGraphIngestionService : IKustoGraphIngestionService
{
    private readonly KustoGraphIngestionCoordinator coordinator;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphIngestionService"/> class.
    /// </summary>
    /// <param name="graphQueryService">The streaming Kusto graph query service.</param>
    /// <param name="graphStore">The app-wide durable graph store.</param>
    public KustoGraphIngestionService(
        IKustoGraphQueryService graphQueryService,
        IGraphStore graphStore)
    {
        coordinator = new KustoGraphIngestionCoordinator(
            graphQueryService,
            graphStore,
            new SqliteStagingSourceFactory());
    }

    /// <inheritdoc />
    public Task<KustoGraphIngestionResult> ExecuteAsync(
        KustoGraphIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        return coordinator.ExecuteAsync(request, cancellationToken);
    }

    private sealed class SqliteStagingSourceFactory : IKustoGraphImportStagingSourceFactory
    {
        public IKustoGraphImportStagingSource Create(
            KustoGraphQueryPlan plan,
            KustoQueryRequest query,
            Guid ingestionId)
        {
            return new KustoGraphImportStagingSource(plan, query, ingestionId);
        }
    }
}
