using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using OpenKustoExplorer.Infrastructure.Graph;
using OpenKustoExplorer.Portable.Graphs;

namespace OpenKustoExplorer.Infrastructure.Tests.Graph;

/// <summary>
/// Verifies the openCypher contract is identical across durable graph backends.
/// </summary>
public sealed class GraphQueryStoreContractTests
{
    /// <summary>
    /// Verifies node labels, property filters, projections, and degree values have parity.
    /// </summary>
    /// <returns>A task that completes when both backends have executed the query.</returns>
    [Fact]
    public async Task NodeMatchFilterAndProjectionHaveParity()
    {
        const string Query = "MATCH (n:User) WHERE n.enabled = 'true' "
            + "RETURN n.canonicalId AS id, n.displayLabel AS label, n.degree AS degree";

        QueryResults results = await ExecuteOnBothBackendsAsync(Query);

        AssertEquivalent(results.Sqlite, results.Json);
        Assert.True(results.Sqlite.Succeeded);
        GraphQueryRow row = Assert.Single(results.Sqlite.Rows);
        Assert.Equal(["alice@example.com", "Alice", "1"], row.Values.Select(value => value.DisplayText));
    }

    /// <summary>
    /// Verifies relationship direction, type filtering, and matched viewport construction have parity.
    /// </summary>
    /// <returns>A task that completes when both backends have executed the query.</returns>
    [Fact]
    public async Task RelationshipDirectionTypeAndViewportHaveParity()
    {
        const string Query = "MATCH (h:Host)<-[r:AuthenticatedTo]-(u:User) "
            + "WHERE type(r) = 'AuthenticatedTo' "
            + "RETURN h.canonicalId AS host, u.canonicalId AS user, type(r) AS relation "
            + "ORDER BY u.canonicalId";

        QueryResults results = await ExecuteOnBothBackendsAsync(Query);

        AssertEquivalent(results.Sqlite, results.Json);
        Assert.True(results.Sqlite.Succeeded);
        Assert.Equal(2, results.Sqlite.Rows.Count);
        Assert.All(results.Sqlite.Rows, row => Assert.Equal("server-1", row.Values[0].DisplayText));
        Assert.Equal(
            ["alice@example.com", "bob@example.com"],
            results.Sqlite.Rows.Select(row => row.Values[1].DisplayText));
        Assert.Equal(3, results.Sqlite.Viewport.Entities.Count);
        Assert.Equal(2, results.Sqlite.Viewport.Relationships.Count);
    }

    /// <summary>
    /// Verifies ordering, limiting, and aggregate projection have parity.
    /// </summary>
    /// <returns>A task that completes when both backends have executed the queries.</returns>
    [Fact]
    public async Task OrderingLimitAndAggregateHaveParity()
    {
        QueryResults ordered = await ExecuteOnBothBackendsAsync(
            "MATCH (n:User) RETURN n.canonicalId AS id ORDER BY n.canonicalId DESC LIMIT 1");
        QueryResults aggregate = await ExecuteOnBothBackendsAsync(
            "MATCH (n) RETURN count(*) AS total");

        AssertEquivalent(ordered.Sqlite, ordered.Json);
        AssertEquivalent(aggregate.Sqlite, aggregate.Json);
        Assert.Equal("bob@example.com", Assert.Single(ordered.Sqlite.Rows).Values[0].DisplayText);
        Assert.Equal("3", Assert.Single(aggregate.Sqlite.Rows).Values[0].DisplayText);
    }

    /// <summary>
    /// Verifies position-aware parse diagnostics have parity.
    /// </summary>
    /// <returns>A task that completes when both backends reject the query.</returns>
    [Fact]
    public async Task ParseDiagnosticHasParity()
    {
        QueryResults results = await ExecuteOnBothBackendsAsync("MATCH (n RETURN n");

        AssertEquivalent(results.Sqlite, results.Json);
        Assert.False(results.Sqlite.Succeeded);
        Assert.Single(results.Sqlite.Diagnostics);
        Assert.Empty(results.Sqlite.Rows);
        Assert.True(results.Sqlite.Viewport.IsEmpty);
    }

    private static async Task<QueryResults> ExecuteOnBothBackendsAsync(string queryText)
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-Graph-Contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);

        try
        {
            using SqliteGraphStore sqliteStore = new(Path.Combine(directoryPath, "graph.db"));
            using JsonGraphStore jsonStore = await JsonGraphStore.CreateAsync(new MemoryGraphSnapshotStore());
            DateTimeOffset discoveredAt = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
            await sqliteStore.ImportAsync(CreateContractBatch(discoveredAt), GraphImportMode.Add);
            await jsonStore.ImportAsync(CreateContractBatch(discoveredAt), GraphImportMode.Add);
            GraphStateSummary sqliteState = await sqliteStore.GetStateAsync();
            GraphStateSummary jsonState = await jsonStore.GetStateAsync();
            GraphQueryResult sqlite = await sqliteStore.ExecuteOpenCypherAsync(
                new GraphQueryRequest(sqliteState.Snapshot, queryText));
            GraphQueryResult json = await jsonStore.ExecuteOpenCypherAsync(
                new GraphQueryRequest(jsonState.Snapshot, queryText));
            return new QueryResults(sqlite, json);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directoryPath, true);
        }
    }

    private static void AssertEquivalent(GraphQueryResult expected, GraphQueryResult actual)
    {
        Assert.Equal(expected.QueryText, actual.QueryText);
        Assert.Equal(expected.Succeeded, actual.Succeeded);
        Assert.Equal(expected.AreRowsTruncated, actual.AreRowsTruncated);
        Assert.Equal(
            expected.Columns.Select(column => (column.Name, column.Kind)),
            actual.Columns.Select(column => (column.Name, column.Kind)));
        Assert.Equal(expected.Rows.Count, actual.Rows.Count);

        for (int index = 0; index < expected.Rows.Count; index++)
        {
            Assert.Equal(
                expected.Rows[index].Values.Select(value => (
                    value.Kind,
                    value.DisplayText,
                    value.Entity,
                    value.Relationship)),
                actual.Rows[index].Values.Select(value => (
                    value.Kind,
                    value.DisplayText,
                    value.Entity,
                    value.Relationship)));
        }

        Assert.Equal(
            expected.Diagnostics.Select(diagnostic => (
                diagnostic.Severity,
                diagnostic.Start,
                diagnostic.Length,
                diagnostic.Message)),
            actual.Diagnostics.Select(diagnostic => (
                diagnostic.Severity,
                diagnostic.Start,
                diagnostic.Length,
                diagnostic.Message)));
        Assert.Equal(expected.Viewport.Center, actual.Viewport.Center);
        Assert.Equal(expected.Viewport.IsTruncated, actual.Viewport.IsTruncated);
        Assert.Equal(
            expected.Viewport.Entities.Select(entity => (
                entity.Entity,
                entity.DisplayLabel,
                entity.FirstDiscoveredAtUtc,
                entity.LastUpdatedAtUtc,
                entity.Degree)),
            actual.Viewport.Entities.Select(entity => (
                entity.Entity,
                entity.DisplayLabel,
                entity.FirstDiscoveredAtUtc,
                entity.LastUpdatedAtUtc,
                entity.Degree)));
        Assert.Equal(expected.Viewport.Relationships, actual.Viewport.Relationships);
    }

    private static GraphImportBatch CreateContractBatch(DateTimeOffset discoveredAt)
    {
        GraphEntityKey alice = new(GraphEntityKind.User, "User", "alice@example.com");
        GraphEntityKey bob = new(GraphEntityKind.User, "User", "bob@example.com");
        GraphEntityKey host = new(GraphEntityKind.Host, "Host", "server-1");
        GraphEvidence evidence = new(
            "occurrence-contract",
            "hash-contract",
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
                "Contract import",
                new Uri("https://cluster.example.com"),
                "Security",
                "Events | make-graph source --> target",
                discoveredAt.AddSeconds(-1),
                discoveredAt),
            [evidence],
            [
                CreateEntity(alice, "Alice", "true", interval, evidence.OccurrenceId),
                CreateEntity(bob, "Bob", "false", interval, evidence.OccurrenceId),
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
                CreateRelationship(alice, host, interval, evidence.OccurrenceId),
                CreateRelationship(bob, host, interval, evidence.OccurrenceId),
            ]);
    }

    private static GraphEntityObservation CreateEntity(
        GraphEntityKey entity,
        string displayLabel,
        string enabled,
        GraphTemporalInterval interval,
        string evidenceId)
    {
        return new GraphEntityObservation(
            Guid.NewGuid(),
            entity,
            displayLabel,
            ["AZUser"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["enabled"] = enabled },
            interval,
            [evidenceId]);
    }

    private static GraphRelationshipObservation CreateRelationship(
        GraphEntityKey source,
        GraphEntityKey target,
        GraphTemporalInterval interval,
        string evidenceId)
    {
        return new GraphRelationshipObservation(
            Guid.NewGuid(),
            new GraphRelationshipKey(source, target, "AuthenticatedTo"),
            ["SIGN_IN"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["method"] = "MFA" },
            interval,
            [evidenceId]);
    }

    private sealed record QueryResults(GraphQueryResult Sqlite, GraphQueryResult Json);

    private sealed class MemoryGraphSnapshotStore : IGraphSnapshotStore
    {
        private string? json;

        public Task<string?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(json);
        }

        public Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            json = snapshotJson;
            return Task.CompletedTask;
        }
    }
}
