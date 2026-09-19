using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Infrastructure.Graph;

namespace OpenKustoExplorer.Infrastructure.Tests.Graph;

/// <summary>
/// Verifies disk-staged Kusto graph exports commit only after complete validation.
/// </summary>
public sealed class KustoGraphIngestionServiceTests
{
    private const string QueryText = "graph(\"SecurityGraph\")";

    /// <summary>
    /// Verifies common source labels retain useful semantic categories for graph presentation.
    /// </summary>
    /// <param name="typeName">The source-provided node type.</param>
    /// <param name="expectedKind">The inferred semantic category.</param>
    /// <returns>A task that completes when the aliased node is read from durable storage.</returns>
    [Theory]
    [InlineData("GitHubUser", GraphEntityKind.User)]
    [InlineData("PublicKey", GraphEntityKind.Credential)]
    [InlineData("IPAddress", GraphEntityKind.IpAddress)]
    [InlineData("Machine", GraphEntityKind.Host)]
    public async Task ExecuteMapsInvestigationTypeAlias(
        string typeName,
        GraphEntityKind expectedKind)
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphIngestionService service = new(
                new StubGraphQueryService(sourceTypeName: typeName),
                store);

            await service.ExecuteAsync(CreateRequest());
            GraphEntitySummary alice = Assert.Single(await store.SearchEntitiesAsync("Alice", 10));

            Assert.Equal(expectedKind, alice.Entity.Kind);
            Assert.Equal(typeName, alice.Entity.TypeName);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies exported rows become source-scoped entities, relationships, and canonical raw evidence.
    /// </summary>
    /// <returns>A task that completes when the staged import is committed.</returns>
    [Fact]
    public async Task ExecuteMapsValidatedExportAndPreservesEvidence()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphIngestionService service = new(new StubGraphQueryService(), store);

            KustoGraphIngestionResult result = await service.ExecuteAsync(CreateRequest());
            GraphStateSummary state = await store.GetStateAsync();
            GraphEntitySummary alice = Assert.Single(await store.SearchEntitiesAsync("Alice", 10));

            Assert.Equal(2, result.Export.NodeCount);
            Assert.Equal(1, result.Export.EdgeCount);
            Assert.Equal(3, result.Import.EvidenceCount);
            Assert.Equal(2, result.Import.EntitiesObserved);
            Assert.Equal(1, result.Import.RelationshipsObserved);
            Assert.Equal(2, state.EntityCount);
            Assert.Equal(1, state.RelationshipCount);
            Assert.Equal(GraphEntityKind.User, alice.Entity.Kind);
            Assert.Equal("alice-id", alice.Entity.CanonicalId);
            Assert.Equal("cluster.example.com/security", alice.Entity.SourceNamespace);
            Assert.Equal(1, alice.Degree);

            using SqliteConnection connection = new($"Data Source={filePath}");
            await connection.OpenAsync();
            using SqliteCommand evidenceCommand = connection.CreateCommand();
            evidenceCommand.CommandText = """
                SELECT payload.schema_json, payload.row_json
                FROM graph_evidence_occurrences AS occurrence
                INNER JOIN graph_evidence_payloads AS payload
                    ON payload.content_hash = occurrence.content_hash
                WHERE occurrence.table_name = 'nodes_export'
                    AND occurrence.row_ordinal = 0;
                """;
            await using SqliteDataReader evidenceReader = await evidenceCommand.ExecuteReaderAsync();

            Assert.True(await evidenceReader.ReadAsync());
            Assert.Equal(
                "{\"columns\":[{\"name\":\"node_hash\",\"type\":\"long\"},{\"name\":\"id\",\"type\":\"string\"},{\"name\":\"name\",\"type\":\"string\"},{\"name\":\"NodeType\",\"type\":\"string\"}]}",
                evidenceReader.GetString(0));
            Assert.Equal(
                "{\"node_hash\":\"1\",\"id\":\"alice-id\",\"name\":\"Alice\",\"NodeType\":\"User\"}",
                evidenceReader.GetString(1));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies exact node values with different inferred types pause before commit and merge when approved.
    /// </summary>
    /// <returns>A task that completes when the resolved import is committed.</returns>
    [Fact]
    public async Task ExecuteMergesSameValueAfterIdentityResolution()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphIngestionService service = new(
                new StubGraphQueryService(includeDuplicateIdentity: true),
                store);
            GraphEntityIdentityConflict? presentedConflict = null;
            KustoGraphIngestionRequest request = CreateRequest(
                (conflict, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    presentedConflict = conflict;
                    return Task.FromResult(GraphIdentityResolutionDecision.Merge);
                });

            KustoGraphIngestionResult result = await service.ExecuteAsync(request);
            GraphStateSummary state = await store.GetStateAsync();
            GraphEntitySummary alice = Assert.Single(await store.SearchEntitiesAsync("Alice", 10));

            Assert.NotNull(presentedConflict);
            Assert.Equal("alice-id", presentedConflict.MatchValue);
            Assert.Equal(["Person", "User"], presentedConflict.Candidates
                .Select(candidate => candidate.Entity.TypeName)
                .Order(StringComparer.Ordinal));
            Assert.Equal(3, result.Export.NodeCount);
            Assert.Equal(2, state.EntityCount);
            Assert.Equal("User", alice.Entity.TypeName);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies identical display names with different identifiers are presented for confirmation and can merge.
    /// </summary>
    /// <returns>A task that completes when the name-matched import is committed.</returns>
    [Fact]
    public async Task ExecuteMergesSameDisplayNameAfterIdentityResolution()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphIngestionService service = new(
                new StubGraphQueryService(
                    includeDuplicateIdentity: true,
                    duplicateCanonicalId: "person-alice-id"),
                store);
            GraphEntityIdentityConflict? presentedConflict = null;
            KustoGraphIngestionRequest request = CreateRequest(
                (conflict, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    presentedConflict = conflict;
                    return Task.FromResult(GraphIdentityResolutionDecision.Merge);
                });

            await service.ExecuteAsync(request);
            GraphStateSummary state = await store.GetStateAsync();

            Assert.NotNull(presentedConflict);
            Assert.Equal(GraphEntityIdentityMatchKind.DisplayLabel, presentedConflict.MatchKind);
            Assert.Equal("alice", presentedConflict.MatchValue);
            Assert.Equal(["alice-id", "person-alice-id"], presentedConflict.Candidates
                .Select(candidate => candidate.Entity.CanonicalId)
                .Order(StringComparer.Ordinal));
            Assert.Equal(2, state.EntityCount);
            Assert.Single(await store.SearchEntitiesAsync("Alice", 10));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies declining a possible match preserves both differently typed nodes.
    /// </summary>
    /// <returns>A task that completes when the separate identities are committed.</returns>
    [Fact]
    public async Task ExecuteKeepsSameValueNodesSeparateWhenRequested()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphIngestionService service = new(
                new StubGraphQueryService(includeDuplicateIdentity: true),
                store);
            KustoGraphIngestionRequest request = CreateRequest(
                (conflict, cancellationToken) =>
                {
                    _ = conflict;
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.FromResult(GraphIdentityResolutionDecision.KeepSeparate);
                });

            await service.ExecuteAsync(request);
            GraphStateSummary state = await store.GetStateAsync();

            Assert.Equal(3, state.EntityCount);
            Assert.Equal(2, (await store.SearchEntitiesAsync("Alice", 10)).Count);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies canceling identity resolution discards the complete staged ingestion.
    /// </summary>
    /// <returns>A task that completes when staged rows are discarded.</returns>
    [Fact]
    public async Task ExecuteCancelsBeforeCommitDuringIdentityResolution()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphIngestionService service = new(
                new StubGraphQueryService(includeDuplicateIdentity: true),
                store);
            KustoGraphIngestionRequest request = CreateRequest(
                (conflict, cancellationToken) =>
                {
                    _ = conflict;
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.FromResult(GraphIdentityResolutionDecision.Cancel);
                });

            await Assert.ThrowsAsync<GraphIdentityResolutionCanceledException>(() =>
                service.ExecuteAsync(request));
            GraphStateSummary state = await store.GetStateAsync();

            Assert.True(state.IsEmpty);
            Assert.Equal(0, state.IngestionCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies a later Add import can merge a differently typed same-value node into the target graph identity.
    /// </summary>
    /// <returns>A task that completes when both imports are committed.</returns>
    [Fact]
    public async Task ExecuteMergesIncomingIdentityIntoExistingSameValueNode()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphIngestionService initialService = new(new StubGraphQueryService(), store);
            await initialService.ExecuteAsync(CreateRequest());
            GraphStateSummary initialState = await store.GetStateAsync();
            KustoGraphIngestionService addService = new(
                new StubGraphQueryService(sourceTypeName: "Person"),
                store);
            GraphEntityIdentityConflict? presentedConflict = null;
            KustoGraphIngestionRequest addRequest = CreateRequest(
                (conflict, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    presentedConflict = conflict;
                    return Task.FromResult(GraphIdentityResolutionDecision.Merge);
                },
                new GraphWriteTarget(initialState.Snapshot));

            await addService.ExecuteAsync(addRequest);
            GraphStateSummary finalState = await store.GetStateAsync();
            GraphEntitySummary alice = Assert.Single(await store.SearchEntitiesAsync("Alice", 10));

            Assert.NotNull(presentedConflict);
            Assert.Contains(presentedConflict.Candidates, candidate =>
                candidate.IsExisting && candidate.Entity.TypeName == "User");
            Assert.Contains(presentedConflict.Candidates, candidate =>
                !candidate.IsExisting && candidate.Entity.TypeName == "Person");
            Assert.Equal(2, finalState.EntityCount);
            Assert.Equal("User", alice.Entity.TypeName);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies a small log graph retains every node and uses node labels as type fallbacks.
    /// </summary>
    /// <returns>A task that completes when the complete graph viewport is projected.</returns>
    [Fact]
    public async Task ExecuteRetainsCompleteLabelTypedLogGraph()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphIngestionService service = new(new StubLogGraphQueryService(), store);

            KustoGraphIngestionResult result = await service.ExecuteAsync(CreateRequest());
            GraphStateSummary state = await store.GetStateAsync();
            GraphViewport viewport = await store.GetViewportAsync(null, 10, 10);
            GraphViewport boundedViewport = await store.GetViewportAsync(null, 3, 10);
            GraphEntitySummary firstIp = Assert.Single(await store.SearchEntitiesAsync("31.56.96.51", 10));
            GraphEntitySummary secondIp = Assert.Single(await store.SearchEntitiesAsync("54.36.149.41", 10));
            GraphEntitySummary resource = Assert.Single(await store.SearchEntitiesAsync("/product/27", 10));

            Assert.Equal(4, result.Export.NodeCount);
            Assert.Equal(3, result.Export.EdgeCount);
            Assert.Equal(4, state.EntityCount);
            Assert.Equal(3, state.RelationshipCount);
            Assert.Equal(4, viewport.Entities.Count);
            Assert.Equal(3, viewport.Relationships.Count);
            Assert.Equal(3, boundedViewport.Entities.Count);
            Assert.True(boundedViewport.IsTruncated);
            Assert.All(boundedViewport.Relationships, relationship =>
            {
                Assert.Contains(boundedViewport.Entities, entity => entity.Entity == relationship.Source);
                Assert.Contains(boundedViewport.Entities, entity => entity.Entity == relationship.Target);
            });
            Assert.Equal("IP address", firstIp.Entity.TypeName);
            Assert.Equal("31.56.96.51", firstIp.DisplayLabel);
            Assert.Equal("IP address", secondIp.Entity.TypeName);
            Assert.Equal("resource", resource.Entity.TypeName);
            Assert.Equal("/product/27", resource.DisplayLabel);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies rows emitted before a terminal query failure never reach durable graph storage.
    /// </summary>
    /// <returns>A task that completes when the failed export is discarded.</returns>
    [Fact]
    public async Task ExecuteDiscardsRowsWhenQueryFailsAfterStreaming()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphIngestionService service = new(
                new StubGraphQueryService(failAfterStreaming: true),
                store);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(CreateRequest()));
            GraphStateSummary state = await store.GetStateAsync();

            Assert.True(state.IsEmpty);
            Assert.Equal(0, state.IngestionCount);
            Assert.Equal(0, state.EvidenceCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies an edge cannot commit when its target hash is absent from the exported nodes table.
    /// </summary>
    /// <returns>A task that completes when endpoint validation rejects the export.</returns>
    [Fact]
    public async Task ExecuteRejectsUnresolvedEdgeEndpoint()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphIngestionService service = new(
                new StubGraphQueryService(useMissingTarget: true),
                store);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.ExecuteAsync(CreateRequest()));
            GraphStateSummary state = await store.GetStateAsync();

            Assert.Contains("unresolved endpoints", exception.Message, StringComparison.Ordinal);
            Assert.True(state.IsEmpty);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    private static KustoGraphIngestionRequest CreateRequest(
        GraphIdentityConflictResolver? identityConflictResolver = null,
        GraphWriteTarget? target = null)
    {
        KustoQueryRequest query = new(new Uri("https://cluster.example.com"), "Security", QueryText);
        KustoGraphQueryPlan plan = new(
            new KustoQuerySelection(QueryText, 0, QueryText.Length),
            KustoGraphSourceKind.GraphFunction,
            "export query",
            "nodes_export",
            "edges_export",
            "node_hash",
            "source_hash",
            "target_hash",
            null,
            null);
        return target is GraphWriteTarget graphTarget
            ? new KustoGraphIngestionRequest(
                query,
                plan,
                graphTarget,
                GraphImportMode.Add,
                GraphIngestionSourceKind.ManualQuery,
                Guid.NewGuid(),
                "Investigation query",
                identityConflictResolver)
            : new KustoGraphIngestionRequest(
                query,
                plan,
                GraphImportMode.Add,
                GraphIngestionSourceKind.ManualQuery,
                Guid.NewGuid(),
                "Investigation query",
                identityConflictResolver);
    }

    private static string CreateTemporaryDirectory()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-GraphIngestion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static void DeleteTemporaryDirectory(string directoryPath)
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(directoryPath, true);
    }

    private sealed class StubGraphQueryService : IKustoGraphQueryService
    {
        private readonly bool failAfterStreaming;
        private readonly string duplicateCanonicalId;
        private readonly bool includeDuplicateIdentity;
        private readonly string sourceTypeName;
        private readonly bool useMissingTarget;

        internal StubGraphQueryService(
            string sourceTypeName = "User",
            bool failAfterStreaming = false,
            bool includeDuplicateIdentity = false,
            string duplicateCanonicalId = "alice-id",
            bool useMissingTarget = false)
        {
            this.sourceTypeName = sourceTypeName;
            this.failAfterStreaming = failAfterStreaming;
            this.includeDuplicateIdentity = includeDuplicateIdentity;
            this.duplicateCanonicalId = duplicateCanonicalId;
            this.useMissingTarget = useMissingTarget;
        }

        public async Task<KustoGraphExportSummary> ExecuteGraphAsync(
            KustoQueryRequest request,
            KustoGraphQueryPlan plan,
            IKustoGraphExportSink sink,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            KustoResultColumn[] nodeColumns =
            [
                new("node_hash", "long"),
                new("id", "string"),
                new("name", "string"),
                new("NodeType", "string"),
            ];
            List<IReadOnlyList<string>> nodeRows =
            [
                ["1", "alice-id", "Alice", sourceTypeName],
                ["2", "server-id", "Server 1", "Host"],
            ];
            if (includeDuplicateIdentity)
            {
                nodeRows.Add(["3", duplicateCanonicalId, "Alice", "Person"]);
            }

            await sink.WriteBatchAsync(
                new KustoGraphExportBatch(
                    KustoGraphExportTableKind.Nodes,
                    nodeRows,
                    true,
                    true,
                    plan.NodeTableName,
                    nodeColumns),
                cancellationToken);

            KustoResultColumn[] edgeColumns =
            [
                new("source_hash", "long"),
                new("target_hash", "long"),
                new("edge_type", "string"),
                new("edge_id", "string"),
            ];
            await sink.WriteBatchAsync(
                new KustoGraphExportBatch(
                    KustoGraphExportTableKind.Edges,
                    [["1", useMissingTarget ? "404" : "2", "AuthenticatedTo", "edge-1"]],
                    true,
                    true,
                    plan.EdgeTableName,
                    edgeColumns),
                cancellationToken);

            if (failAfterStreaming)
            {
                throw new InvalidOperationException("Kusto graph query failed after returning partial rows.");
            }

            int nodeCount = includeDuplicateIdentity ? 3 : 2;
            return new KustoGraphExportSummary(nodeCount, 1, TimeSpan.FromMilliseconds(25));
        }
    }

    private sealed class StubLogGraphQueryService : IKustoGraphQueryService
    {
        public async Task<KustoGraphExportSummary> ExecuteGraphAsync(
            KustoQueryRequest request,
            KustoGraphQueryPlan plan,
            IKustoGraphExportSink sink,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            KustoResultColumn[] nodeColumns =
            [
                new("node_hash", "long"),
                new("nodeId", "string"),
                new("label", "string"),
            ];
            await sink.WriteBatchAsync(
                new KustoGraphExportBatch(
                    KustoGraphExportTableKind.Nodes,
                    [
                        ["1207539687289059547", "31.56.96.51", "IP address"],
                        ["-3794469666640219336", "/product/27", "resource"],
                        ["6689426799178276641", "/product/42", "resource"],
                        ["7991813526547606428", "54.36.149.41", "IP address"],
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
                new("ipAddress", "string"),
                new("timestamp", "datetime"),
                new("httpVerb", "string"),
                new("resource", "string"),
            ];
            await sink.WriteBatchAsync(
                new KustoGraphExportBatch(
                    KustoGraphExportTableKind.Edges,
                    [
                        ["1207539687289059547", "-3794469666640219336", "31.56.96.51", "2019-01-22T00:24:16.0000000Z", "GET", "/product/27"],
                        ["1207539687289059547", "6689426799178276641", "31.56.96.51", "2019-01-22T00:25:17.0000000Z", "GET", "/product/42"],
                        ["7991813526547606428", "-3794469666640219336", "54.36.149.41", "2019-01-22T00:26:14.0000000Z", "GET", "/product/27"],
                    ],
                    true,
                    true,
                    plan.EdgeTableName,
                    edgeColumns),
                cancellationToken);

            return new KustoGraphExportSummary(4, 3, TimeSpan.FromMilliseconds(25));
        }
    }
}
