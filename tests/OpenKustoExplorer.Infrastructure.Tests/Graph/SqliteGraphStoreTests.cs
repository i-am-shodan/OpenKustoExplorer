using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using OpenKustoExplorer.Infrastructure.Graph;

namespace OpenKustoExplorer.Infrastructure.Tests.Graph;

/// <summary>
/// Verifies transactional persistence for the app-wide investigation graph.
/// </summary>
public sealed class SqliteGraphStoreTests
{
    /// <summary>
    /// Verifies read-only openCypher projects typed values and a matched graph viewport.
    /// </summary>
    /// <returns>A task that completes when the path query is materialized.</returns>
    [Fact]
    public async Task OpenCypherProjectsTypedPathResultsAndSchema()
    {
        const string Query = "MATCH (u:User)-[r:AuthenticatedTo]->(h:Host) "
            + "WHERE u.enabled = 'true' "
            + "RETURN u, type(r) AS relation, h.canonicalId AS host LIMIT 10";
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 24, 16, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteGraphStore store = new(filePath);
            await store.ImportAsync(
                CreateConnectedBatch("cypher", "hash-cypher", "{\"event\":1}", discoveredAt),
                GraphImportMode.Add);
            GraphStateSummary state = await store.GetStateAsync();
            GraphQuerySchema schema = await store.GetSchemaAsync(state.Snapshot);
            GraphQueryResult result = await store.ExecuteOpenCypherAsync(new GraphQueryRequest(
                state.Snapshot,
                Query));

            Assert.Contains(schema.NodeLabels, entry => entry.Name == "User" && entry.Count == 1);
            Assert.Contains(schema.NodeLabels, entry => entry.Name == "Host" && entry.Count == 1);
            Assert.Contains(schema.RelationshipTypes, entry => entry.Name == "AuthenticatedTo" && entry.Count == 1);
            Assert.True(result.Succeeded);
            Assert.Empty(result.Diagnostics);
            Assert.Equal(["u", "relation", "host"], result.Columns.Select(column => column.Name));
            GraphQueryRow row = Assert.Single(result.Rows);
            Assert.Equal(GraphQueryValueKind.Entity, row.Values[0].Kind);
            Assert.Equal("alice@example.com", row.Values[0].Entity?.CanonicalId);
            Assert.Equal("AuthenticatedTo", row.Values[1].DisplayText);
            Assert.Equal("server-1", row.Values[2].DisplayText);
            Assert.Equal(2, result.Viewport.Entities.Count);
            Assert.Single(result.Viewport.Relationships);
            Assert.False(result.AreRowsTruncated);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies an analyst label is graph-scoped, searchable, durable, and protected from later source observations.
    /// </summary>
    /// <returns>A task that completes after rename, reimport, and database reopen.</returns>
    [Fact]
    public async Task EntityDisplayLabelOverrideSurvivesImportsAndRestart()
    {
        const string AnalystLabel = "Incident owner";
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        GraphEntityKey user = new(GraphEntityKind.User, "User", "alice@example.com");

        try
        {
            using (SqliteGraphStore store = new(filePath))
            {
                await store.ImportAsync(
                    CreateConnectedBatch("label-first", "hash-label-first", "{\"event\":1}", discoveredAt),
                    GraphImportMode.Add);
                GraphStateSummary state = await store.GetStateAsync();

                GraphEntitySummary renamed = await store.SetEntityDisplayLabelAsync(
                    state.Snapshot,
                    user,
                    AnalystLabel);
                GraphEntityDetails details = Assert.IsType<GraphEntityDetails>(
                    await store.GetEntityDetailsAsync(user));
                GraphViewport viewport = await store.GetViewportAsync(user, 10, 10);
                GraphEntitySummary visibleUser = Assert.Single(
                    viewport.Entities,
                    entity => entity.Entity == user);

                Assert.Equal(AnalystLabel, renamed.DisplayLabel);
                Assert.Equal(AnalystLabel, details.Summary.DisplayLabel);
                Assert.Equal(AnalystLabel, visibleUser.DisplayLabel);
                Assert.Single(await store.SearchEntitiesAsync(AnalystLabel, 10));

                GraphStateSummary secondGraph = await store.CreateGraphAsync("Separate label graph", null);
                await store.ImportAsync(
                    new GraphWriteTarget(secondGraph.Snapshot),
                    CreateConnectedBatch(
                        "label-other-graph",
                        "hash-label-other-graph",
                        "{\"event\":2}",
                        discoveredAt.AddMinutes(30)),
                    GraphImportMode.Add);
                GraphEntitySummary secondGraphUser = Assert.Single(await store.SearchEntitiesAsync("Alice", 10));
                Assert.Equal("Alice", secondGraphUser.DisplayLabel);
                Assert.Empty(await store.SearchEntitiesAsync(AnalystLabel, 10));

                GraphStateSummary reactivated = await store.ActivateGraphAsync(state.GraphId);
                await store.ImportAsync(
                    new GraphWriteTarget(reactivated.Snapshot),
                    CreateConnectedBatch(
                        "label-second",
                        "hash-label-second",
                        "{\"event\":3}",
                        discoveredAt.AddHours(1),
                        userDisplayLabel: "Directory Alice"),
                    GraphImportMode.Replace);
                GraphEntityDetails updatedDetails = Assert.IsType<GraphEntityDetails>(
                    await store.GetEntityDetailsAsync(user));

                Assert.Equal(AnalystLabel, updatedDetails.Summary.DisplayLabel);
                await using SqliteConnection connection = new($"Data Source={filePath}");
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT display_label
                    FROM graph_entity_observations
                    WHERE canonical_id = 'alice@example.com'
                    ORDER BY discovered_at_utc DESC
                    LIMIT 1;
                    """;
                Assert.Equal("Directory Alice", await command.ExecuteScalarAsync() as string);
            }

            using SqliteGraphStore reopenedStore = new(filePath);
            GraphEntitySummary reopened = Assert.Single(await reopenedStore.SearchEntitiesAsync(AnalystLabel, 10));
            Assert.Equal(AnalystLabel, reopened.DisplayLabel);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies aggregates, ordering, and explicit graph snapshots remain isolated.
    /// </summary>
    /// <returns>A task that completes when both named graph queries execute.</returns>
    [Fact]
    public async Task OpenCypherAggregatesAndKeepsNamedGraphsIsolated()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 24, 16, 30, 0, TimeSpan.Zero);

        try
        {
            using SqliteGraphStore store = new(filePath);
            await store.ImportAsync(
                CreateConnectedBatch("first-cypher", "hash-first-cypher", "{\"event\":1}", discoveredAt),
                GraphImportMode.Add);
            GraphStateSummary first = await store.GetStateAsync();
            GraphStateSummary second = await store.CreateGraphAsync("Second query graph", null);
            await store.ImportAsync(
                new GraphWriteTarget(second.Snapshot),
                CreateSingleEntityBatch(
                    "second-cypher",
                    "hash-second-cypher",
                    discoveredAt.AddMinutes(1),
                    canonicalId: "second-only",
                    displayLabel: "Second only"),
                GraphImportMode.Add);

            GraphQueryResult firstCount = await store.ExecuteOpenCypherAsync(new GraphQueryRequest(
                first.Snapshot,
                "MATCH (n) RETURN count(*) AS total"));
            GraphQueryResult secondRows = await store.ExecuteOpenCypherAsync(new GraphQueryRequest(
                second.Snapshot,
                "MATCH (n:Database) RETURN n.canonicalId AS id ORDER BY n.canonicalId DESC"));

            Assert.True(firstCount.Succeeded);
            Assert.Equal(GraphQueryValueKind.WholeNumber, Assert.Single(firstCount.Columns).Kind);
            Assert.Equal("2", Assert.Single(firstCount.Rows).Values[0].DisplayText);
            Assert.Equal(2, firstCount.Viewport.Entities.Count);
            Assert.True(secondRows.Succeeded);
            Assert.Equal("second-only", Assert.Single(secondRows.Rows).Values[0].DisplayText);
            Assert.Single(secondRows.Viewport.Entities);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies write clauses and undefined variables return diagnostics without mutating graph data.
    /// </summary>
    /// <returns>A task that completes after invalid queries are rejected.</returns>
    [Fact]
    public async Task OpenCypherRejectsWritesAndInvalidVariables()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            GraphStateSummary before = await store.GetStateAsync();
            GraphQueryResult write = await store.ExecuteOpenCypherAsync(new GraphQueryRequest(
                before.Snapshot,
                "MATCH (n) DELETE n RETURN n"));
            GraphQueryResult invalidVariable = await store.ExecuteOpenCypherAsync(new GraphQueryRequest(
                before.Snapshot,
                "MATCH (n) RETURN missing.canonicalId"));
            GraphStateSummary after = await store.GetStateAsync();

            Assert.False(write.Succeeded);
            Assert.Contains(write.Diagnostics, diagnostic => diagnostic.Severity == GraphQueryDiagnosticSeverity.Error);
            Assert.False(invalidVariable.Succeeded);
            Assert.Contains(
                invalidVariable.Diagnostics,
                diagnostic => diagnostic.Message.Contains("not defined", StringComparison.Ordinal));
            Assert.Equal(before.GenerationId, after.GenerationId);
            Assert.Equal(before.EntityCount, after.EntityCount);
            Assert.Equal(before.RelationshipCount, after.RelationshipCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies named graphs can be created, edited, activated, isolated, and deleted safely.
    /// </summary>
    /// <returns>A task that completes when catalog lifecycle behavior is verified.</returns>
    [Fact]
    public async Task CatalogManagesNamedGraphsAndKeepsOneActive()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 24, 13, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteGraphStore store = new(filePath);
            GraphCatalog initialCatalog = await store.GetCatalogAsync();
            GraphCatalogEntry defaultGraph = Assert.Single(initialCatalog.Graphs);
            GraphStateSummary initialState = await store.GetStateAsync();

            Assert.Equal("Default graph", defaultGraph.Name);
            Assert.Equal(defaultGraph.GraphId, initialCatalog.ActiveGraphId);
            Assert.Equal(defaultGraph.GraphId, initialState.GraphId);
            Assert.True(initialState.IsEmpty);

            GraphStateSummary created = await store.CreateGraphAsync(
                "  Threat investigation  ",
                "  Privileged access review  ");
            Assert.Equal("Threat investigation", created.GraphName);
            Assert.Equal("Privileged access review", created.GraphDescription);
            Assert.Equal(created.GraphId, (await store.GetCatalogAsync()).ActiveGraphId);
            await Assert.ThrowsAsync<SqliteException>(() => store.CreateGraphAsync(
                "threat INVESTIGATION",
                null));

            GraphCatalogEntry updated = await store.UpdateGraphAsync(
                created.GraphId,
                "Identity investigation",
                "Updated scope");
            Assert.Equal("Identity investigation", updated.Name);
            Assert.Equal("Updated scope", updated.Description);

            await store.ImportAsync(
                CreateSingleEntityBatch("named", "hash-named", discoveredAt),
                GraphImportMode.Add);
            Assert.Single(await store.SearchEntitiesAsync("security", 10));

            GraphStateSummary activatedDefault = await store.ActivateGraphAsync(defaultGraph.GraphId);
            Assert.True(activatedDefault.IsEmpty);
            Assert.Empty(await store.SearchEntitiesAsync("security", 10));

            GraphStateSummary activatedCreated = await store.ActivateGraphAsync(created.GraphId);
            Assert.Equal(1, activatedCreated.EntityCount);
            Assert.Single(await store.SearchEntitiesAsync("security", 10));

            GraphCatalog remaining = await store.DeleteGraphAsync(created.GraphId);
            Assert.Equal(defaultGraph.GraphId, remaining.ActiveGraphId);
            Assert.Single(remaining.Graphs);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteGraphAsync(defaultGraph.GraphId));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies deleting all graphs atomically removes every retained row and creates one new empty default graph.
    /// </summary>
    /// <returns>A task that completes after the catalog is reset.</returns>
    [Fact]
    public async Task DeleteAllGraphsReplacesCatalogWithFreshEmptyDefault()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteGraphStore store = new(filePath);
            GraphStateSummary first = await store.GetStateAsync();
            await store.ImportAsync(
                CreateConnectedBatch("first", "hash-delete-all-first", "{\"event\":1}", discoveredAt),
                GraphImportMode.Add);
            GraphStateSummary second = await store.CreateGraphAsync("Second graph", "Temporary investigation");
            await store.ImportAsync(
                CreateSingleEntityBatch(
                    "second",
                    "hash-delete-all-second",
                    discoveredAt.AddMinutes(1),
                    canonicalId: "second-only",
                    displayLabel: "Second only"),
                GraphImportMode.Add);

            GraphCatalog resetCatalog = await store.DeleteAllGraphsAsync();
            GraphCatalogEntry resetGraph = Assert.Single(resetCatalog.Graphs);
            GraphStateSummary resetState = await store.GetStateAsync();

            Assert.Equal("Default graph", resetGraph.Name);
            Assert.Equal(resetGraph.GraphId, resetCatalog.ActiveGraphId);
            Assert.NotEqual(first.GraphId, resetGraph.GraphId);
            Assert.NotEqual(second.GraphId, resetGraph.GraphId);
            Assert.Equal(resetGraph.Snapshot, resetState.Snapshot);
            Assert.True(resetState.IsEmpty);
            Assert.Empty(await store.SearchEntitiesAsync("security", 10));
            Assert.Empty(await store.SearchEntitiesAsync("Second only", 10));

            await using SqliteConnection connection = new($"Data Source={filePath}");
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM graph_evidence_payloads;";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies pinned writes do not follow an active-graph switch and stale generations are rejected.
    /// </summary>
    /// <returns>A task that completes when pinned write isolation is verified.</returns>
    [Fact]
    public async Task PinnedWritesStayWithTheirGraphAndRejectStaleGenerations()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 24, 13, 30, 0, TimeSpan.Zero);

        try
        {
            using SqliteGraphStore store = new(filePath);
            GraphStateSummary firstGraph = await store.GetStateAsync();
            GraphWriteTarget firstTarget = new(firstGraph.Snapshot);
            GraphStateSummary secondGraph = await store.CreateGraphAsync("Second graph", null);

            GraphImportResult imported = await store.ImportAsync(
                firstTarget,
                CreateSingleEntityBatch("pinned", "hash-pinned", discoveredAt),
                GraphImportMode.Add);

            Assert.Equal(firstGraph.GraphId, imported.GraphId);
            Assert.Equal(secondGraph.GraphId, (await store.GetStateAsync()).GraphId);
            Assert.Equal(1, (await store.GetStateAsync(firstGraph.Snapshot)).EntityCount);
            GraphEntitySummary inactiveEntity = Assert.Single(await store.SearchEntitiesAsync(
                firstGraph.Snapshot,
                "security",
                10));
            Assert.Single((await store.GetViewportAsync(firstGraph.Snapshot, null, 10, 10)).Entities);
            Assert.Single((await store.GetNeighborhoodAsync(
                firstGraph.Snapshot,
                [inactiveEntity.Entity],
                1,
                10,
                10)).Entities);
            Assert.NotNull(await store.GetEntityDetailsAsync(firstGraph.Snapshot, inactiveEntity.Entity));

            GraphStateSummary cleared = await store.ClearAsync(firstTarget);
            Assert.Equal(firstGraph.GraphId, cleared.GraphId);
            Assert.NotEqual(firstTarget.ExpectedGenerationId, cleared.GenerationId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportAsync(
                firstTarget,
                CreateSingleEntityBatch("stale", "hash-stale", discoveredAt.AddMinutes(1)),
                GraphImportMode.Add));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SearchEntitiesAsync(
                firstGraph.Snapshot,
                "security",
                10));
            Assert.Equal(secondGraph.GraphId, (await store.GetStateAsync()).GraphId);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies a version-one database is migrated into a default named graph without losing retained data.
    /// </summary>
    /// <returns>A task that completes when legacy migration is verified.</returns>
    [Fact]
    public async Task VersionOneMigrationPreservesRetainedGraphData()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 24, 14, 0, 0, TimeSpan.Zero);
        Guid activeGenerationId;

        try
        {
            using (SqliteGraphStore store = new(filePath))
            {
                await store.ImportAsync(
                    CreateConnectedBatch("legacy-first", "hash-legacy-first", "{\"event\":1}", discoveredAt),
                    GraphImportMode.Add);
                GraphImportResult replacement = await store.ImportAsync(
                    CreateSingleEntityBatch(
                        "legacy-replace",
                        "hash-legacy-replace",
                        discoveredAt.AddHours(1)),
                    GraphImportMode.Replace);
                activeGenerationId = replacement.GenerationId;
            }

            DowngradeCatalogToVersionOne(filePath);

            using SqliteGraphStore migratedStore = new(filePath);
            GraphCatalog catalog = await migratedStore.GetCatalogAsync();
            GraphCatalogEntry migratedGraph = Assert.Single(catalog.Graphs);
            GraphStateSummary state = await migratedStore.GetStateAsync();

            Assert.Equal("Default graph", migratedGraph.Name);
            Assert.Equal(activeGenerationId, migratedGraph.GenerationId);
            Assert.Equal(activeGenerationId, state.GenerationId);
            Assert.Equal(1, state.EntityCount);
            Assert.Equal(1, state.IngestionCount);
            Assert.Single(await migratedStore.SearchEntitiesAsync("security", 10));

            await using SqliteConnection connection = new($"Data Source={filePath}");
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT user_version FROM pragma_user_version),
                    (SELECT COUNT(*) FROM graph_generations),
                    (SELECT COUNT(*) FROM graph_generations WHERE graph_id IS NOT NULL),
                    (SELECT COUNT(*) FROM pragma_foreign_key_check);
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(3, reader.GetInt32(0));
            Assert.Equal(2, reader.GetInt64(1));
            Assert.Equal(2, reader.GetInt64(2));
            Assert.Equal(0, reader.GetInt64(3));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies default and centered viewports return bounded active-generation relationship neighborhoods.
    /// </summary>
    /// <returns>A task that completes when graph viewport projection is verified.</returns>
    [Fact]
    public async Task ViewportReturnsBoundedActiveGenerationNeighborhood()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 23, 7, 0, 0, TimeSpan.Zero);
        GraphEntityKey host = new(GraphEntityKind.Host, "Host", "server-1");

        try
        {
            using SqliteGraphStore store = new(filePath);
            await store.ImportAsync(
                CreateConnectedBatch("viewport", "hash-viewport", "{\"event\":1}", discoveredAt),
                GraphImportMode.Add);

            GraphViewport defaultViewport = await store.GetViewportAsync(null, 10, 10);
            GraphViewport centeredViewport = await store.GetViewportAsync(host, 10, 10);
            GraphViewport truncatedViewport = await store.GetViewportAsync(host, 1, 10);

            Assert.Equal(2, defaultViewport.Entities.Count);
            Assert.Single(defaultViewport.Relationships);
            Assert.NotNull(defaultViewport.Center);
            Assert.Equal(host, centeredViewport.Center);
            Assert.Equal(["Alice", "Server 1"], centeredViewport.Entities
                .Select(entity => entity.DisplayLabel)
                .Order(StringComparer.Ordinal));
            GraphRelationshipKey relationship = Assert.Single(centeredViewport.Relationships);
            Assert.Equal("AuthenticatedTo", relationship.TypeName);
            Assert.Equal(GraphEntityKind.User, relationship.Source.Kind);
            Assert.Equal(host, relationship.Target);
            Assert.Single(truncatedViewport.Entities);
            Assert.Empty(truncatedViewport.Relationships);
            Assert.True(truncatedViewport.IsTruncated);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                store.GetViewportAsync(null, 501, 10));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                store.GetViewportAsync(null, 10, 2_001));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies a snapshot overview can project every entity above the bounded active-view limit.
    /// </summary>
    /// <returns>A task that completes when the complete snapshot is projected.</returns>
    [Fact]
    public async Task SnapshotViewportReturnsAllRequestedEntities()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
        GraphEntityKey[] entities = Enumerable.Range(1, 600)
            .Select(index => new GraphEntityKey(GraphEntityKind.Device, "Device", $"device-{index}"))
            .ToArray();

        try
        {
            using SqliteGraphStore store = new(filePath);
            await store.ImportAsync(
                CreateGraphBatch(entities, [], discoveredAt),
                GraphImportMode.Add);
            GraphStateSummary state = await store.GetStateAsync();

            GraphViewport viewport = await store.GetViewportAsync(
                state.Snapshot,
                null,
                entities.Length,
                1);

            Assert.Equal(entities.Length, viewport.Entities.Count);
            Assert.False(viewport.IsTruncated);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies depth and multiple centers produce one bounded merged neighborhood.
    /// </summary>
    /// <returns>A task that completes when graph traversal is projected.</returns>
    [Fact]
    public async Task NeighborhoodSupportsDepthAndMultipleCenters()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        GraphEntityKey first = new(GraphEntityKind.Device, "Device", "first");
        GraphEntityKey second = new(GraphEntityKind.Device, "Device", "second");
        GraphEntityKey third = new(GraphEntityKind.Device, "Device", "third");
        GraphEntityKey fourth = new(GraphEntityKind.Device, "Device", "fourth");

        try
        {
            using SqliteGraphStore store = new(filePath);
            await store.ImportAsync(
                CreateChainBatch([first, second, third, fourth], discoveredAt),
                GraphImportMode.Add);

            GraphViewport oneHop = await store.GetNeighborhoodAsync([second], 1, 10, 10);
            GraphViewport twoHops = await store.GetNeighborhoodAsync([second], 2, 10, 10);
            GraphViewport merged = await store.GetNeighborhoodAsync([first, fourth], 1, 10, 10);

            Assert.Equal(["first", "second", "third"], oneHop.Entities
                .Select(entity => entity.Entity.CanonicalId)
                .Order(StringComparer.Ordinal));
            Assert.Equal(2, oneHop.Relationships.Count);
            Assert.Equal(4, twoHops.Entities.Count);
            Assert.Equal(3, twoHops.Relationships.Count);
            Assert.Equal(4, merged.Entities.Count);
            Assert.Equal(3, merged.Relationships.Count);
            Assert.Equal(first, merged.Center);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies route lookup keeps equal shortest routes while excluding unrelated branches.
    /// </summary>
    /// <returns>A task that completes when route traversal is projected.</returns>
    [Fact]
    public async Task RoutesReturnOnlyShortestConnectingSubgraph()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);
        GraphEntityKey start = new(GraphEntityKind.Device, "Device", "start");
        GraphEntityKey left = new(GraphEntityKind.Device, "Device", "left");
        GraphEntityKey right = new(GraphEntityKind.Device, "Device", "right");
        GraphEntityKey destination = new(GraphEntityKind.Device, "Device", "destination");
        GraphEntityKey branch = new(GraphEntityKind.Device, "Device", "branch");
        GraphEntityKey isolated = new(GraphEntityKind.Device, "Device", "isolated");
        GraphRelationshipKey[] relationships =
        [
            new(start, left, "Connects"),
            new(left, destination, "Connects"),
            new(start, right, "Connects"),
            new(right, destination, "Connects"),
            new(left, branch, "Connects"),
        ];

        try
        {
            using SqliteGraphStore store = new(filePath);
            await store.ImportAsync(
                CreateGraphBatch(
                    [start, left, right, destination, branch, isolated],
                    relationships,
                    discoveredAt),
                GraphImportMode.Add);
            GraphStateSummary state = await store.GetStateAsync();

            GraphRouteResult routes = await store.FindRoutesAsync(
                state.Snapshot,
                start,
                destination,
                10,
                10);
            GraphRouteResult boundedRoutes = await store.FindRoutesAsync(
                state.Snapshot,
                start,
                destination,
                3,
                2);
            GraphRouteResult disconnected = await store.FindRoutesAsync(
                state.Snapshot,
                start,
                isolated,
                10,
                10);

            Assert.True(routes.IsConnected);
            Assert.Equal(2, routes.ShortestHopCount);
            Assert.Equal(["destination", "left", "right", "start"], routes.Viewport.Entities
                .Select(entity => entity.Entity.CanonicalId)
                .Order(StringComparer.Ordinal));
            Assert.Equal(4, routes.Viewport.Relationships.Count);
            Assert.DoesNotContain(routes.Viewport.Entities, entity => entity.Entity == branch);
            Assert.False(routes.Viewport.IsTruncated);
            Assert.True(boundedRoutes.IsConnected);
            Assert.Equal(3, boundedRoutes.Viewport.Entities.Count);
            Assert.Equal(2, boundedRoutes.Viewport.Relationships.Count);
            Assert.Contains(boundedRoutes.Viewport.Entities, entity => entity.Entity == start);
            Assert.Contains(boundedRoutes.Viewport.Entities, entity => entity.Entity == destination);
            Assert.True(boundedRoutes.Viewport.IsTruncated);
            Assert.False(disconnected.IsConnected);
            Assert.Null(disconnected.ShortestHopCount);
            Assert.Equal(2, disconnected.Viewport.Entities.Count);
            Assert.Empty(disconnected.Viewport.Relationships);
            using CancellationTokenSource cancellationSource = new();
            await cancellationSource.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.FindRoutesAsync(
                state.Snapshot,
                start,
                destination,
                10,
                10,
                cancellationSource.Token));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies search returns active-generation identities with degree while enforcing result bounds.
    /// </summary>
    /// <returns>A task that completes when entity search is verified.</returns>
    [Fact]
    public async Task SearchReturnsBoundedActiveGenerationSummaries()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 23, 8, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteGraphStore store = new(filePath);
            await store.ImportAsync(
                CreateConnectedBatch("search", "hash-search", "{\"event\":1}", discoveredAt),
                GraphImportMode.Add);

            GraphEntitySummary alice = Assert.Single(await store.SearchEntitiesAsync("ali", 10));

            Assert.Equal("Alice", alice.DisplayLabel);
            Assert.Equal(GraphEntityKind.User, alice.Entity.Kind);
            Assert.Equal(1, alice.Degree);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SearchEntitiesAsync("alice", 501));

            await store.ImportAsync(
                CreateSingleEntityBatch("new-generation", "hash-new", discoveredAt.AddHours(1)),
                GraphImportMode.Replace);

            Assert.Empty(await store.SearchEntitiesAsync("alice", 10));
            Assert.Single(await store.SearchEntitiesAsync("secur", 10));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies search matches case-insensitive substrings inside node labels and identifiers.
    /// </summary>
    /// <returns>A task that completes when the partial identifier match is returned.</returns>
    [Fact]
    public async Task SearchMatchesCaseInsensitiveIdentifierSubstring()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 24, 11, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteGraphStore store = new(filePath);
            await store.ImportAsync(
                CreateSingleEntityBatch(
                    "partial-search",
                    "hash-partial-search",
                    discoveredAt,
                    canonicalId: "github:HildeTeamTNT",
                    displayLabel: "github:HildeTeamTNT"),
                GraphImportMode.Add);

            GraphEntitySummary result = Assert.Single(await store.SearchEntitiesAsync("teamtnt", 10));

            Assert.Equal("github:HildeTeamTNT", result.Entity.CanonicalId);
            Assert.Equal("github:HildeTeamTNT", result.DisplayLabel);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies entity details return the latest properties and cumulative retained counts.
    /// </summary>
    /// <returns>A task that completes when entity detail projection is verified.</returns>
    [Fact]
    public async Task EntityDetailsReturnLatestPropertiesAndCounts()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 23, 8, 30, 0, TimeSpan.Zero);
        GraphEntityKey user = new(GraphEntityKind.User, "User", "alice@example.com");

        try
        {
            using SqliteGraphStore store = new(filePath);
            await store.ImportAsync(
                CreateConnectedBatch("details-first", "hash-details-first", "{\"event\":1}", discoveredAt),
                GraphImportMode.Add);
            await store.ImportAsync(
                CreateConnectedBatch(
                    "details-second",
                    "hash-details-second",
                    "{\"event\":2}",
                    discoveredAt.AddMinutes(5),
                    "false"),
                GraphImportMode.Add);

            GraphEntityDetails details = Assert.IsType<GraphEntityDetails>(
                await store.GetEntityDetailsAsync(user));

            Assert.Equal("Alice", details.Summary.DisplayLabel);
            Assert.Equal(1, details.Summary.Degree);
            Assert.Equal(["AZUser"], details.SourceLabels);
            GraphEntityProperty property = Assert.Single(details.Properties);
            Assert.Equal("enabled", property.Name);
            Assert.Equal("false", property.Value);
            Assert.Equal(2, details.ObservationCount);
            Assert.Equal(2, details.EvidenceCount);
            Assert.Equal(2, details.Evidence.Count);
            Assert.Equal("Query details-second", details.Evidence[0].SourceName);
            Assert.Equal("Security", details.Evidence[0].DatabaseName);
            Assert.Equal("Events | make-graph source --> target", details.Evidence[0].QueryText);
            Assert.Equal("PrimaryResult", details.Evidence[0].TableName);
            Assert.Equal("{\"event\":2}", details.Evidence[0].RowJson);
            Assert.False(details.IsEvidenceTruncated);

            GraphRelationshipKey relationship = new(
                user,
                new GraphEntityKey(GraphEntityKind.Host, "Host", "server-1"),
                "AuthenticatedTo");
            GraphRelationshipDetails relationshipDetails = Assert.IsType<GraphRelationshipDetails>(
                await store.GetRelationshipDetailsAsync(relationship));
            Assert.Equal(["SIGN_IN"], relationshipDetails.SourceLabels);
            GraphEntityProperty relationshipProperty = Assert.Single(relationshipDetails.Properties);
            Assert.Equal("method", relationshipProperty.Name);
            Assert.Equal("MFA", relationshipProperty.Value);
            Assert.Equal(discoveredAt, relationshipDetails.FirstDiscoveredAtUtc);
            Assert.Equal(discoveredAt.AddMinutes(5), relationshipDetails.LastUpdatedAtUtc);
            Assert.Equal(2, relationshipDetails.ObservationCount);
            Assert.Equal(2, relationshipDetails.EvidenceCount);
            Assert.Equal("{\"event\":2}", relationshipDetails.Evidence[0].RowJson);
            Assert.False(relationshipDetails.IsEvidenceTruncated);
            Assert.Null(await store.GetEntityDetailsAsync(
                new GraphEntityKey(GraphEntityKind.User, "User", "missing@example.com")));
            Assert.Null(await store.GetRelationshipDetailsAsync(new GraphRelationshipKey(
                user,
                new GraphEntityKey(GraphEntityKind.Host, "Host", "missing"),
                "AuthenticatedTo")));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies retained ingestion points render cumulative graph state across replaced generations.
    /// </summary>
    /// <returns>A task that completes after historical viewports are projected.</returns>
    [Fact]
    public async Task TimelineRetainsHistoricalStatesAcrossGenerations()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset earlyAt = new(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset laterAt = earlyAt.AddHours(1);

        try
        {
            using SqliteGraphStore store = new(filePath);
            GraphStateSummary initialState = await store.GetStateAsync();
            await store.ImportAsync(
                CreateSingleEntityBatch(
                    "early",
                    "hash-timeline-early",
                    earlyAt,
                    canonicalId: "early-node",
                    displayLabel: "Early node"),
                GraphImportMode.Add);
            await store.ImportAsync(
                CreateConnectedBatch(
                    "later",
                    "hash-timeline-later",
                    "{\"event\":\"later\"}",
                    laterAt),
                GraphImportMode.Replace);

            IReadOnlyList<GraphTimelinePoint> timeline = await store.GetTimelineAsync(
                initialState.GraphId,
                20);
            GraphTimelinePoint earlyPoint = Assert.Single(
                timeline,
                point => point.SourceName == "Query early");
            GraphTimelinePoint laterPoint = Assert.Single(
                timeline,
                point => point.SourceName == "Query later");

            GraphViewport earlyViewport = await store.GetTimelineViewportAsync(earlyPoint, 200, 500);
            GraphViewport laterViewport = await store.GetTimelineViewportAsync(laterPoint, 200, 500);

            GraphEntitySummary earlyEntity = Assert.Single(earlyViewport.Entities);
            Assert.Equal("early-node", earlyEntity.Entity.CanonicalId);
            Assert.Empty(earlyViewport.Relationships);
            GraphEntityDetails earlyDetails = Assert.IsType<GraphEntityDetails>(
                await store.GetEntityDetailsAsync(earlyPoint.Snapshot, earlyEntity.Entity));
            GraphEvidenceRecord earlyEvidence = Assert.Single(earlyDetails.Evidence);
            Assert.Equal("Query early", earlyEvidence.SourceName);
            Assert.Equal("Events | make-graph source --> target", earlyEvidence.QueryText);
            Assert.Equal(2, laterViewport.Entities.Count);
            Assert.Single(laterViewport.Relationships);
            Assert.NotEqual(earlyPoint.Snapshot.GenerationId, laterPoint.Snapshot.GenerationId);
            Assert.Equal((await store.GetStateAsync()).Snapshot, laterPoint.Snapshot);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies repeated observations merge identities while retaining every ingestion and evidence occurrence.
    /// </summary>
    /// <returns>A task that completes when graph persistence is verified.</returns>
    [Fact]
    public async Task AddMergesIdentitiesAndRetainsEvidenceOccurrences()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset firstDiscovery = new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteGraphStore store = new(filePath);
            Assert.True((await store.GetStateAsync()).IsEmpty);

            GraphImportResult first = await store.ImportAsync(
                CreateConnectedBatch("first", "hash-a", "{\"event\":1}", firstDiscovery),
                GraphImportMode.Add);
            GraphImportResult second = await store.ImportAsync(
                CreateConnectedBatch("second", "hash-a", "{\"event\":1}", firstDiscovery.AddHours(1)),
                GraphImportMode.Add);
            GraphStateSummary state = await store.GetStateAsync();

            Assert.Equal(2, first.EntitiesAdded);
            Assert.Equal(1, first.RelationshipsAdded);
            Assert.Equal(0, second.EntitiesAdded);
            Assert.Equal(0, second.RelationshipsAdded);
            Assert.Equal(2, state.EntityCount);
            Assert.Equal(1, state.RelationshipCount);
            Assert.Equal(2, state.IngestionCount);
            Assert.Equal(2, state.EvidenceCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies Replace and Clear switch the active generation without exposing prior counts.
    /// </summary>
    /// <returns>A task that completes when generation rotation is verified.</returns>
    [Fact]
    public async Task ReplaceAndClearRotateTheActiveGeneration()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteGraphStore store = new(filePath);
            GraphImportResult added = await store.ImportAsync(
                CreateConnectedBatch("add", "hash-add", "{\"event\":1}", discoveredAt),
                GraphImportMode.Add);
            GraphImportResult replaced = await store.ImportAsync(
                CreateSingleEntityBatch("replace", "hash-replace", discoveredAt.AddHours(1)),
                GraphImportMode.Replace);
            GraphStateSummary replacementState = await store.GetStateAsync();
            GraphStateSummary clearedState = await store.ClearAsync();

            Assert.NotEqual(added.GenerationId, replaced.GenerationId);
            Assert.Equal(replaced.GenerationId, replacementState.GenerationId);
            Assert.Equal(1, replacementState.EntityCount);
            Assert.Equal(0, replacementState.RelationshipCount);
            Assert.Equal(1, replacementState.IngestionCount);
            Assert.NotEqual(replacementState.GenerationId, clearedState.GenerationId);
            Assert.True(clearedState.IsEmpty);
            Assert.Equal(0, clearedState.IngestionCount);
            Assert.Equal(0, clearedState.EvidenceCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies a conflicting payload for an existing content hash rolls back the complete ingestion.
    /// </summary>
    /// <returns>A task that completes when rollback behavior is verified.</returns>
    [Fact]
    public async Task EvidenceHashConflictRollsBackTheImport()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset discoveredAt = new(2026, 7, 23, 11, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteGraphStore store = new(filePath);
            await store.ImportAsync(
                CreateSingleEntityBatch("first", "shared-hash", discoveredAt),
                GraphImportMode.Add);
            GraphStateSummary beforeConflict = await store.GetStateAsync();
            GraphImportBatch conflict = CreateSingleEntityBatch(
                "conflict",
                "shared-hash",
                discoveredAt.AddMinutes(1),
                rowJson: "{\"different\":true}");

            await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(conflict, GraphImportMode.Add));
            GraphStateSummary afterConflict = await store.GetStateAsync();

            Assert.Equal(beforeConflict.GenerationId, afterConflict.GenerationId);
            Assert.Equal(beforeConflict.IngestionCount, afterConflict.IngestionCount);
            Assert.Equal(beforeConflict.EvidenceCount, afterConflict.EvidenceCount);
            Assert.Equal(beforeConflict.EntityCount, afterConflict.EntityCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies cancellation before import leaves the active graph unchanged.
    /// </summary>
    /// <returns>A task that completes when cancellation behavior is verified.</returns>
    [Fact]
    public async Task CanceledImportLeavesTheGraphUnchanged()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            GraphStateSummary beforeCancellation = await store.GetStateAsync();
            using CancellationTokenSource cancellationSource = new();
            await cancellationSource.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ImportAsync(
                CreateSingleEntityBatch("canceled", "hash-canceled", DateTimeOffset.UtcNow),
                GraphImportMode.Add,
                cancellationSource.Token));
            GraphStateSummary afterCancellation = await store.GetStateAsync();

            Assert.Equal(beforeCancellation.GenerationId, afterCancellation.GenerationId);
            Assert.True(afterCancellation.IsEmpty);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    private static GraphImportBatch CreateConnectedBatch(
        string suffix,
        string contentHash,
        string rowJson,
        DateTimeOffset discoveredAt,
        string userEnabled = "true",
        string userDisplayLabel = "Alice")
    {
        GraphEvidence evidence = CreateEvidence(suffix, contentHash, rowJson);
        GraphEntityKey user = new(GraphEntityKind.User, "User", "alice@example.com");
        GraphEntityKey host = new(GraphEntityKind.Host, "Host", "server-1");
        GraphTemporalInterval temporalInterval = new(discoveredAt);
        GraphEntityObservation userObservation = new(
            Guid.NewGuid(),
            user,
            userDisplayLabel,
            ["AZUser"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["enabled"] = userEnabled },
            temporalInterval,
            [evidence.OccurrenceId]);
        GraphEntityObservation hostObservation = new(
            Guid.NewGuid(),
            host,
            "Server 1",
            ["Device"],
            new Dictionary<string, string>(StringComparer.Ordinal),
            temporalInterval,
            [evidence.OccurrenceId]);
        GraphRelationshipObservation relationshipObservation = new(
            Guid.NewGuid(),
            new GraphRelationshipKey(user, host, "AuthenticatedTo"),
            ["SIGN_IN"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["method"] = "MFA" },
            temporalInterval,
            [evidence.OccurrenceId]);
        return new GraphImportBatch(
            CreateIngestion(suffix, discoveredAt),
            [evidence],
            [userObservation, hostObservation],
            [relationshipObservation]);
    }

    private static GraphImportBatch CreateChainBatch(
        IReadOnlyList<GraphEntityKey> entities,
        DateTimeOffset discoveredAt)
    {
        GraphEvidence evidence = CreateEvidence("chain", "hash-chain", "{\"chain\":true}");
        GraphTemporalInterval temporalInterval = new(discoveredAt);
        GraphEntityObservation[] entityObservations = entities
            .Select(entity => new GraphEntityObservation(
                Guid.NewGuid(),
                entity,
                entity.CanonicalId,
                [entity.TypeName],
                new Dictionary<string, string>(StringComparer.Ordinal),
                temporalInterval,
                [evidence.OccurrenceId]))
            .ToArray();
        GraphRelationshipObservation[] relationships = entities
            .Zip(entities.Skip(1), (source, target) => new GraphRelationshipObservation(
                Guid.NewGuid(),
                new GraphRelationshipKey(source, target, "PointsTo"),
                [],
                new Dictionary<string, string>(StringComparer.Ordinal),
                temporalInterval,
                [evidence.OccurrenceId]))
            .ToArray();
        return new GraphImportBatch(
            CreateIngestion("chain", discoveredAt),
            [evidence],
            entityObservations,
            relationships);
    }

    private static GraphImportBatch CreateGraphBatch(
        IReadOnlyList<GraphEntityKey> entities,
        IReadOnlyList<GraphRelationshipKey> relationships,
        DateTimeOffset discoveredAt)
    {
        GraphEvidence evidence = CreateEvidence("routes", "hash-routes", "{\"routes\":true}");
        GraphTemporalInterval temporalInterval = new(discoveredAt);
        GraphEntityObservation[] entityObservations = entities
            .Select(entity => new GraphEntityObservation(
                Guid.NewGuid(),
                entity,
                entity.CanonicalId,
                [entity.TypeName],
                new Dictionary<string, string>(StringComparer.Ordinal),
                temporalInterval,
                [evidence.OccurrenceId]))
            .ToArray();
        GraphRelationshipObservation[] relationshipObservations = relationships
            .Select(relationship => new GraphRelationshipObservation(
                Guid.NewGuid(),
                relationship,
                [],
                new Dictionary<string, string>(StringComparer.Ordinal),
                temporalInterval,
                [evidence.OccurrenceId]))
            .ToArray();
        return new GraphImportBatch(
            CreateIngestion("routes", discoveredAt),
            [evidence],
            entityObservations,
            relationshipObservations);
    }

    private static GraphEvidence CreateEvidence(string suffix, string contentHash, string rowJson)
    {
        return new GraphEvidence(
            $"occurrence-{suffix}",
            contentHash,
            "PrimaryResult",
            0,
            "{\"columns\":[\"source\",\"target\"]}",
            rowJson);
    }

    private static GraphIngestion CreateIngestion(string suffix, DateTimeOffset discoveredAt)
    {
        return new GraphIngestion(
            Guid.NewGuid(),
            GraphIngestionSourceKind.ManualQuery,
            Guid.NewGuid(),
            $"Query {suffix}",
            new Uri("https://cluster.example.com"),
            "Security",
            "Events | make-graph source --> target",
            discoveredAt.AddSeconds(-1),
            discoveredAt);
    }

    private static GraphImportBatch CreateSingleEntityBatch(
        string suffix,
        string contentHash,
        DateTimeOffset discoveredAt,
        string rowJson = "{\"entity\":1}",
        string canonicalId = "security",
        string displayLabel = "Security")
    {
        GraphEvidence evidence = CreateEvidence(suffix, contentHash, rowJson);
        GraphEntityObservation observation = new(
            Guid.NewGuid(),
            new GraphEntityKey(GraphEntityKind.Database, "Database", canonicalId),
            displayLabel,
            ["Database"],
            new Dictionary<string, string>(StringComparer.Ordinal),
            new GraphTemporalInterval(discoveredAt),
            [evidence.OccurrenceId]);
        return new GraphImportBatch(
            CreateIngestion(suffix, discoveredAt),
            [evidence],
            [observation],
            []);
    }

    private static void DowngradeCatalogToVersionOne(string filePath)
    {
        SqliteConnection.ClearAllPools();
        using SqliteConnection connection = new($"Data Source={filePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = OFF;
            BEGIN IMMEDIATE;

            CREATE TABLE graph_generations_v1 (
                id TEXT PRIMARY KEY,
                created_at_utc TEXT NOT NULL,
                last_updated_at_utc TEXT NOT NULL
            );
            INSERT INTO graph_generations_v1 (id, created_at_utc, last_updated_at_utc)
            SELECT id, created_at_utc, last_updated_at_utc
            FROM graph_generations;

            CREATE TABLE graph_state_v1 (
                singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
                active_generation_id TEXT NOT NULL,
                FOREIGN KEY (active_generation_id) REFERENCES graph_generations(id)
            );
            INSERT INTO graph_state_v1 (singleton_id, active_generation_id)
            SELECT 1, catalog.active_generation_id
            FROM graph_state state
            INNER JOIN graph_catalog catalog ON catalog.id = state.active_graph_id
            WHERE state.singleton_id = 1;

            DROP TABLE graph_state;
            DROP TABLE graph_catalog;
            DROP TABLE graph_generations;
            ALTER TABLE graph_generations_v1 RENAME TO graph_generations;
            ALTER TABLE graph_state_v1 RENAME TO graph_state;
            PRAGMA user_version = 1;
            COMMIT;
            PRAGMA foreign_keys = ON;
            """;
        command.ExecuteNonQuery();
    }

    private static string CreateTemporaryDirectory()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-Graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static void DeleteTemporaryDirectory(string directoryPath)
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(directoryPath, true);
    }
}
