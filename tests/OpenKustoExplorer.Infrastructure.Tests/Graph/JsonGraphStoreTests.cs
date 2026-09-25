using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Portable.Graphs;

namespace OpenKustoExplorer.Infrastructure.Tests.Graph;

/// <summary>
/// Verifies the portable graph aggregate used by browser storage.
/// </summary>
public sealed class JsonGraphStoreTests
{
    /// <summary>
    /// Verifies imported graph behavior and evidence survive a complete JSON reload.
    /// </summary>
    /// <returns>A task that completes after the reloaded graph is queried.</returns>
    [Fact]
    public async Task ImportSupportsDetailsSearchRoutesAndReload()
    {
        DateTimeOffset discoveredAt = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        MemoryGraphSnapshotStore snapshotStore = new();
        GraphEntityKey user = new(GraphEntityKind.User, "User", "alice@example.com");
        GraphEntityKey host = new(GraphEntityKind.Host, "Host", "server-1");

        using (JsonGraphStore store = await JsonGraphStore.CreateAsync(snapshotStore))
        {
            GraphImportResult imported = await store.ImportAsync(
                CreateConnectedBatch(discoveredAt, user, host),
                GraphImportMode.Add);
            GraphStateSummary state = await store.GetStateAsync();
            GraphEntityDetails details = Assert.IsType<GraphEntityDetails>(
                await store.GetEntityDetailsAsync(user));
            GraphEntitySummary renamed = await store.SetEntityDisplayLabelAsync(
                state.Snapshot,
                user,
                "Incident owner");
            GraphRouteResult route = await store.FindRoutesAsync(
                state.Snapshot,
                user,
                host,
                10,
                10);

            Assert.Equal(2, imported.EntitiesAdded);
            Assert.Equal(1, imported.RelationshipsAdded);
            Assert.Equal(2, state.EntityCount);
            Assert.Equal("Alice", details.Summary.DisplayLabel);
            Assert.Equal("true", Assert.Single(details.Properties).Value);
            Assert.Equal("{\"event\":1}", Assert.Single(details.Evidence).RowJson);
            Assert.Equal("Incident owner", renamed.DisplayLabel);
            Assert.True(route.IsConnected);
            Assert.Equal(1, route.ShortestHopCount);
        }

        using JsonGraphStore reloaded = await JsonGraphStore.CreateAsync(snapshotStore);
        GraphEntitySummary searchResult = Assert.Single(
            await reloaded.SearchEntitiesAsync("incident", 10));
        GraphViewport viewport = await reloaded.GetViewportAsync(host, 10, 10);

        Assert.Equal(user, searchResult.Entity);
        Assert.Equal(2, viewport.Entities.Count);
        Assert.Single(viewport.Relationships);
    }

    /// <summary>
    /// Verifies a failed durable write rolls the in-memory graph back.
    /// </summary>
    /// <returns>A task that completes after the failed import is inspected.</returns>
    [Fact]
    public async Task FailedSnapshotSaveRollsBackImport()
    {
        MemoryGraphSnapshotStore snapshotStore = new() { FailNextSave = true };
        using JsonGraphStore store = await JsonGraphStore.CreateAsync(snapshotStore);
        GraphStateSummary before = await store.GetStateAsync();

        await Assert.ThrowsAsync<IOException>(() => store.ImportAsync(
            CreateConnectedBatch(
                new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
                new GraphEntityKey(GraphEntityKind.User, "User", "alice@example.com"),
                new GraphEntityKey(GraphEntityKind.Host, "Host", "server-1")),
            GraphImportMode.Add));

        GraphStateSummary after = await store.GetStateAsync();
        Assert.Equal(before.GenerationId, after.GenerationId);
        Assert.True(after.IsEmpty);
        Assert.Null(snapshotStore.Json);
    }

    /// <summary>
    /// Verifies invalid durable JSON is offered to the host recovery callback before an empty graph is used.
    /// </summary>
    /// <returns>A task that completes after the recovered store is inspected.</returns>
    [Fact]
    public async Task InvalidSnapshotInvokesRecoveryCallback()
    {
        const string InvalidJson = "{\"version\":999}";
        MemoryGraphSnapshotStore snapshotStore = new() { Json = InvalidJson };
        string? recoveredJson = null;

        using JsonGraphStore store = await JsonGraphStore.CreateAsync(
            snapshotStore,
            invalidSnapshotHandler: (json, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                recoveredJson = json;
                return Task.CompletedTask;
            });
        GraphStateSummary state = await store.GetStateAsync();

        Assert.Equal(InvalidJson, recoveredJson);
        Assert.True(state.IsEmpty);
    }

    private static GraphImportBatch CreateConnectedBatch(
        DateTimeOffset discoveredAt,
        GraphEntityKey user,
        GraphEntityKey host)
    {
        GraphEvidence evidence = new(
            "occurrence-1",
            "hash-1",
            "PrimaryResult",
            0,
            "{\"columns\":[\"source\",\"target\"]}",
            "{\"event\":1}");
        GraphTemporalInterval interval = new(discoveredAt);
        return new GraphImportBatch(
            new GraphIngestion(
                Guid.NewGuid(),
                GraphIngestionSourceKind.ManualQuery,
                Guid.NewGuid(),
                "Query import",
                new Uri("https://cluster.example.com"),
                "Security",
                "Events | make-graph source --> target",
                discoveredAt.AddSeconds(-1),
                discoveredAt),
            [evidence],
            [
                new GraphEntityObservation(
                    Guid.NewGuid(),
                    user,
                    "Alice",
                    ["AZUser"],
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["enabled"] = "true" },
                    interval,
                    [evidence.OccurrenceId]),
                new GraphEntityObservation(
                    Guid.NewGuid(),
                    host,
                    "Server 1",
                    ["Device"],
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    interval,
                    [evidence.OccurrenceId]),
            ],
            [
                new GraphRelationshipObservation(
                    Guid.NewGuid(),
                    new GraphRelationshipKey(user, host, "AuthenticatedTo"),
                    ["SIGN_IN"],
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["method"] = "MFA" },
                    interval,
                    [evidence.OccurrenceId]),
            ]);
    }

    private sealed class MemoryGraphSnapshotStore : IGraphSnapshotStore
    {
        internal bool FailNextSave { get; set; }

        internal string? Json { get; set; }

        public Task<string?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Json);
        }

        public Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Simulated storage failure.");
            }

            Json = snapshotJson;
            return Task.CompletedTask;
        }
    }
}
