using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Portable.Graphs;

namespace OpenKustoExplorer.Infrastructure.Tests.Graph;

/// <summary>
/// Verifies shared graph ingestion with the bounded portable staging provider.
/// </summary>
public sealed class BufferedKustoGraphIngestionTests
{
    private const string QueryText = "graph(\"SecurityGraph\")";

    /// <summary>
    /// Verifies streamed Browser-safe batches become durable graph observations and evidence.
    /// </summary>
    /// <returns>A task that completes when the imported graph is queried.</returns>
    [Fact]
    public async Task ExecuteImportsNormalizedBatchesIntoPortableStore()
    {
        using JsonGraphStore store = await JsonGraphStore.CreateAsync(new MemoryGraphSnapshotStore());
        KustoGraphIngestionCoordinator coordinator = new(
            new StubGraphQueryService(),
            store,
            new BufferedKustoGraphImportStagingSourceFactory());

        KustoGraphIngestionResult result = await coordinator.ExecuteAsync(CreateRequest());
        GraphEntitySummary alice = Assert.Single(await store.SearchEntitiesAsync("Alice", 10));
        GraphViewport viewport = await store.GetViewportAsync(alice.Entity, 10, 10);
        GraphEntityDetails details = Assert.IsType<GraphEntityDetails>(
            await store.GetEntityDetailsAsync(alice.Entity));

        Assert.Equal(2, result.Import.EntitiesObserved);
        Assert.Equal(1, result.Import.RelationshipsObserved);
        Assert.Equal(GraphEntityKind.User, alice.Entity.Kind);
        Assert.Equal("alice-id", alice.Entity.CanonicalId);
        Assert.Equal(2, viewport.Entities.Count);
        Assert.Single(viewport.Relationships);
        Assert.Contains(details.Evidence, evidence => evidence.RowJson.Contains("Alice", StringComparison.Ordinal));
    }

    private static KustoGraphIngestionRequest CreateRequest()
    {
        KustoQuerySelection selection = new(QueryText, 0, QueryText.Length);
        KustoGraphQueryPlan plan = new(
            selection,
            KustoGraphSourceKind.GraphFunction,
            "export query",
            "nodes_export",
            "edges_export",
            "node_hash",
            "source_hash",
            "target_hash",
            null,
            null);
        return new KustoGraphIngestionRequest(
            new KustoQueryRequest(
                new Uri("https://cluster.example.com"),
                "Security",
                QueryText),
            plan,
            GraphImportMode.Add,
            GraphIngestionSourceKind.ManualQuery,
            Guid.NewGuid(),
            "Browser query");
    }

    private sealed class MemoryGraphSnapshotStore : IGraphSnapshotStore
    {
        public Task<string?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }

        public Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class StubGraphQueryService : IKustoGraphQueryService
    {
        public async Task<KustoGraphExportSummary> ExecuteGraphAsync(
            KustoQueryRequest request,
            KustoGraphQueryPlan plan,
            IKustoGraphExportSink sink,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            KustoResultColumn[] nodeColumns =
            [
                new("node_hash", "long"),
                new("id", "string"),
                new("name", "string"),
                new("type", "string"),
            ];
            await sink.WriteBatchAsync(
                new KustoGraphExportBatch(
                    KustoGraphExportTableKind.Nodes,
                    [
                        ["1", "alice-id", "Alice", "User"],
                        ["2", "server-id", "Server 1", "Host"],
                    ],
                    true,
                    true,
                    plan.NodeTableName,
                    nodeColumns),
                cancellationToken);
            KustoResultColumn[] edgeColumns =
            [
                new("source_hash", "long"),
                new("target_hash", "long"),
                new("relationship", "string"),
            ];
            await sink.WriteBatchAsync(
                new KustoGraphExportBatch(
                    KustoGraphExportTableKind.Edges,
                    [["1", "2", "AuthenticatedTo"]],
                    true,
                    true,
                    plan.EdgeTableName,
                    edgeColumns),
                cancellationToken);

            return new KustoGraphExportSummary(2, 1, TimeSpan.FromMilliseconds(10));
        }
    }
}
