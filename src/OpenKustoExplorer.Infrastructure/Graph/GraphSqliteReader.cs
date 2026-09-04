using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Reads count-only graph state and active-generation identifiers from SQLite.
/// </summary>
internal static class GraphSqliteReader
{
    private const int MaximumEvidenceRecords = 25;

    /// <summary>
    /// Reads retained generation starts and ingestion completions for one named graph.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="graphId">The named graph to inspect.</param>
    /// <param name="maximumPoints">The maximum number of points to return.</param>
    /// <returns>Retained timeline points ordered newest first.</returns>
    internal static IReadOnlyList<GraphTimelinePoint> ReadTimeline(
        SqliteConnection connection,
        Guid graphId,
        int maximumPoints)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPoints);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT generation_id, timestamp_utc, ingestion_id, source_kind, source_name
            FROM (
                SELECT
                    generation.id AS generation_id,
                    generation.created_at_utc AS timestamp_utc,
                    NULL AS ingestion_id,
                    NULL AS source_kind,
                    'Generation started' AS source_name
                FROM graph_generations generation
                WHERE generation.graph_id = $graphId
                UNION ALL
                SELECT
                    ingestion.generation_id,
                    ingestion.completed_at_utc,
                    ingestion.id,
                    ingestion.source_kind,
                    ingestion.source_name
                FROM graph_ingestions ingestion
                INNER JOIN graph_generations generation
                    ON generation.id = ingestion.generation_id
                WHERE generation.graph_id = $graphId
            ) timeline
            ORDER BY timestamp_utc DESC, ingestion_id DESC
            LIMIT $maximumPoints;
            """;
        command.Parameters.AddWithValue("$graphId", graphId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$maximumPoints", maximumPoints);
        List<GraphTimelinePoint> points = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            Guid? ingestionId = reader.IsDBNull(2)
                ? null
                : Guid.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            GraphIngestionSourceKind? sourceKind = reader.IsDBNull(3)
                ? null
                : (GraphIngestionSourceKind)reader.GetInt32(3);
            points.Add(new GraphTimelinePoint(
                new GraphSnapshot(
                    graphId,
                    Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture)),
                ParseTimestamp(reader.GetString(1)),
                ingestionId,
                sourceKind,
                reader.GetString(4)));
        }

        return points.AsReadOnly();
    }

    /// <summary>
    /// Reads a bounded historical overview containing graph data known by one retained timeline point.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="point">The retained graph timeline point.</param>
    /// <param name="maximumEntityCount">The maximum number of entities to return.</param>
    /// <param name="maximumRelationshipCount">The maximum number of relationships to return.</param>
    /// <param name="cancellationToken">A token that cancels row projection.</param>
    /// <returns>The bounded historical graph overview.</returns>
    internal static GraphViewport ReadTimelineViewport(
        SqliteConnection connection,
        GraphTimelinePoint point,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(point);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelationshipCount);
        ValidateRetainedTimelinePoint(connection, point);
        List<GraphEntitySummary> entities = ReadTimelineEntities(
            connection,
            point,
            maximumEntityCount,
            cancellationToken);
        HashSet<GraphEntityKey> selectedEntities = entities.Select(entity => entity.Entity).ToHashSet();
        List<GraphRelationshipKey> relationships = ReadTimelineRelationships(
            connection,
            point,
            selectedEntities,
            maximumRelationshipCount,
            cancellationToken);
        (long entityCount, long relationshipCount) = ReadTimelineCounts(connection, point);
        bool isTruncated = entityCount > entities.Count || relationshipCount > relationships.Count;
        GraphEntityKey? center = entities.Count > 0 ? entities[0].Entity : null;
        return new GraphViewport(center, entities, relationships, isTruncated);
    }

    /// <summary>
    /// Gets the active graph generation inside an optional transaction.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The optional current transaction.</param>
    /// <returns>The active graph generation identifier.</returns>
    internal static Guid GetActiveGenerationId(
        SqliteConnection connection,
        SqliteTransaction? transaction = null)
    {
        return GetActiveSnapshot(connection, transaction).GenerationId;
    }

    /// <summary>
    /// Gets the active named graph and generation inside an optional transaction.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The optional current transaction.</param>
    /// <returns>The active graph snapshot.</returns>
    internal static GraphSnapshot GetActiveSnapshot(
        SqliteConnection connection,
        SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT catalog.id, catalog.active_generation_id
            FROM graph_state state
            INNER JOIN graph_catalog catalog ON catalog.id = state.active_graph_id
            WHERE state.singleton_id = 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidDataException("The graph database has no active named graph generation.");
        }

        return new GraphSnapshot(
            Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            Guid.Parse(reader.GetString(1), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Reads counts and timestamps for the active graph generation.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <returns>The active graph state.</returns>
    internal static GraphStateSummary ReadState(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return ReadState(connection, GetActiveSnapshot(connection));
    }

    /// <summary>
    /// Reads counts and timestamps for one current named graph snapshot.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The graph generation to read.</param>
    /// <returns>The snapshot's graph state.</returns>
    internal static GraphStateSummary ReadState(SqliteConnection connection, GraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                catalog.id,
                catalog.name,
                catalog.description,
                generation.id,
                generation.created_at_utc,
                generation.last_updated_at_utc,
                (SELECT COUNT(*) FROM graph_entities entity
                    WHERE entity.generation_id = generation.id),
                (SELECT COUNT(*) FROM graph_relationships relationship
                    WHERE relationship.generation_id = generation.id),
                (SELECT COUNT(*) FROM graph_ingestions ingestion
                    WHERE ingestion.generation_id = generation.id),
                (SELECT COUNT(*) FROM graph_evidence_occurrences evidence
                    WHERE evidence.generation_id = generation.id)
            FROM graph_catalog catalog
            INNER JOIN graph_generations generation
                ON generation.id = catalog.active_generation_id
            WHERE catalog.id = $graphId
                AND catalog.active_generation_id = $generationId;
            """;
        command.Parameters.AddWithValue("$graphId", snapshot.GraphId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$generationId",
            snapshot.GenerationId.ToString("D", CultureInfo.InvariantCulture));
        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("The selected graph generation is no longer current.");
        }

        return new GraphStateSummary(
            Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            reader.GetString(1),
            reader.GetString(2),
            Guid.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
            ParseTimestamp(reader.GetString(4)),
            ParseTimestamp(reader.GetString(5)),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9));
    }

    /// <summary>
    /// Reads the latest retained properties and cumulative counts for an active-generation entity.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="entity">The entity to inspect.</param>
    /// <returns>The entity details, or <see langword="null"/> when the entity is not active.</returns>
    internal static GraphEntityDetails? ReadEntityDetails(
        SqliteConnection connection,
        GraphEntityKey entity)
    {
        return ReadEntityDetails(connection, GetActiveSnapshot(connection), entity);
    }

    /// <summary>
    /// Reads retained properties and cumulative counts for an entity in one graph snapshot.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The graph generation to inspect.</param>
    /// <param name="entity">The entity to inspect.</param>
    /// <returns>The entity details, or <see langword="null"/> when absent.</returns>
    internal static GraphEntityDetails? ReadEntityDetails(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        GraphEntityKey entity)
    {
        ArgumentNullException.ThrowIfNull(connection);
        GraphSqliteCatalogReader.ValidateRetainedSnapshot(connection, snapshot);
        Guid generationId = snapshot.GenerationId;
        GraphEntitySummary? summary = ReadEntitySummary(connection, generationId, entity);

        if (summary is null)
        {
            return null;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH observations AS (
                SELECT
                    observation.id,
                    observation.source_labels_json,
                    observation.properties_json,
                    observation.discovered_at_utc
                FROM graph_entity_observations observation
                WHERE observation.generation_id = $generationId
                    AND observation.kind = $kind
                    AND observation.type_name = $typeName
                    AND observation.canonical_id = $canonicalId
                    AND observation.source_namespace = $sourceNamespace
            )
            SELECT
                latest.source_labels_json,
                latest.properties_json,
                (SELECT COUNT(*) FROM observations),
                (
                    SELECT COUNT(DISTINCT evidence.occurrence_id)
                    FROM observations retained
                    INNER JOIN graph_entity_observation_evidence evidence
                        ON evidence.observation_id = retained.id
                )
            FROM observations latest
            ORDER BY latest.discovered_at_utc DESC, latest.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", (int)entity.Kind);
        command.Parameters.AddWithValue("$typeName", entity.TypeName);
        command.Parameters.AddWithValue("$canonicalId", entity.CanonicalId);
        command.Parameters.AddWithValue("$sourceNamespace", entity.SourceNamespace);
        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidDataException($"Graph entity '{entity}' has no retained observations.");
        }

        IReadOnlyList<string> sourceLabels = GraphSqliteJson.ReadStrings(reader.GetString(0));
        IReadOnlyList<GraphEntityProperty> properties = GraphSqliteJson.ReadProperties(reader.GetString(1));
        long observationCount = reader.GetInt64(2);
        long evidenceCount = reader.GetInt64(3);
        reader.Close();
        IReadOnlyList<GraphEvidenceRecord> evidence = ReadEntityEvidence(
            connection,
            snapshot,
            entity);
        return new GraphEntityDetails(
            summary,
            sourceLabels,
            properties,
            observationCount,
            evidenceCount,
            evidence);
    }

    /// <summary>
    /// Reads retained properties and bounded evidence for an active-generation relationship.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="relationship">The relationship to inspect.</param>
    /// <returns>The relationship details, or <see langword="null"/> when absent.</returns>
    internal static GraphRelationshipDetails? ReadRelationshipDetails(
        SqliteConnection connection,
        GraphRelationshipKey relationship)
    {
        return ReadRelationshipDetails(connection, GetActiveSnapshot(connection), relationship);
    }

    /// <summary>
    /// Reads retained properties and bounded evidence for a relationship in one graph snapshot.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The graph generation to inspect.</param>
    /// <param name="relationship">The relationship to inspect.</param>
    /// <returns>The relationship details, or <see langword="null"/> when absent.</returns>
    internal static GraphRelationshipDetails? ReadRelationshipDetails(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        GraphRelationshipKey relationship)
    {
        ArgumentNullException.ThrowIfNull(connection);
        GraphSqliteCatalogReader.ValidateRetainedSnapshot(connection, snapshot);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH observations AS (
                SELECT
                    observation.id,
                    observation.source_labels_json,
                    observation.properties_json,
                    observation.discovered_at_utc
                FROM graph_relationship_observations observation
                WHERE observation.generation_id = $generationId
                    AND observation.source_kind = $sourceKind
                    AND observation.source_type_name = $sourceTypeName
                    AND observation.source_canonical_id = $sourceCanonicalId
                    AND observation.source_namespace = $sourceNamespace
                    AND observation.target_kind = $targetKind
                    AND observation.target_type_name = $targetTypeName
                    AND observation.target_canonical_id = $targetCanonicalId
                    AND observation.target_namespace = $targetNamespace
                    AND observation.relationship_type = $relationshipType
                    AND observation.discriminator = $discriminator
            )
            SELECT
                relationship.first_discovered_at_utc,
                relationship.last_updated_at_utc,
                latest.source_labels_json,
                latest.properties_json,
                (SELECT COUNT(*) FROM observations),
                (
                    SELECT COUNT(DISTINCT evidence.occurrence_id)
                    FROM observations retained
                    INNER JOIN graph_relationship_observation_evidence evidence
                        ON evidence.observation_id = retained.id
                )
            FROM graph_relationships relationship
            INNER JOIN observations latest ON 1 = 1
            WHERE relationship.generation_id = $generationId
                AND relationship.source_kind = $sourceKind
                AND relationship.source_type_name = $sourceTypeName
                AND relationship.source_canonical_id = $sourceCanonicalId
                AND relationship.source_namespace = $sourceNamespace
                AND relationship.target_kind = $targetKind
                AND relationship.target_type_name = $targetTypeName
                AND relationship.target_canonical_id = $targetCanonicalId
                AND relationship.target_namespace = $targetNamespace
                AND relationship.relationship_type = $relationshipType
                AND relationship.discriminator = $discriminator
            ORDER BY latest.discovered_at_utc DESC, latest.id DESC
            LIMIT 1;
            """;
        AddRelationshipParameters(command, snapshot.GenerationId, relationship);
        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        DateTimeOffset firstDiscoveredAtUtc = ParseTimestamp(reader.GetString(0));
        DateTimeOffset lastUpdatedAtUtc = ParseTimestamp(reader.GetString(1));
        IReadOnlyList<string> sourceLabels = GraphSqliteJson.ReadStrings(reader.GetString(2));
        IReadOnlyList<GraphEntityProperty> properties = GraphSqliteJson.ReadProperties(reader.GetString(3));
        long observationCount = reader.GetInt64(4);
        long evidenceCount = reader.GetInt64(5);
        reader.Close();
        IReadOnlyList<GraphEvidenceRecord> evidence = ReadRelationshipEvidence(
            connection,
            snapshot,
            relationship);
        return new GraphRelationshipDetails(
            relationship,
            sourceLabels,
            properties,
            firstDiscoveredAtUtc,
            lastUpdatedAtUtc,
            observationCount,
            evidenceCount,
            evidence);
    }

    /// <summary>
    /// Reads a bounded active-generation overview or one-hop entity neighborhood.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="requestedCenter">The requested center, or <see langword="null"/> for a degree-ranked center.</param>
    /// <param name="maximumEntityCount">The maximum number of entities.</param>
    /// <param name="maximumRelationshipCount">The maximum number of relationships.</param>
    /// <param name="cancellationToken">A token that cancels row projection.</param>
    /// <returns>The bounded active-generation viewport.</returns>
    internal static GraphViewport ReadViewport(
        SqliteConnection connection,
        GraphEntityKey? requestedCenter,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        return ReadViewport(
            connection,
            GetActiveSnapshot(connection),
            requestedCenter,
            maximumEntityCount,
            maximumRelationshipCount,
            cancellationToken);
    }

    /// <summary>
    /// Reads a bounded overview or one-hop viewport from one graph snapshot.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The graph generation to project.</param>
    /// <param name="requestedCenter">The requested center, or <see langword="null"/> for an overview.</param>
    /// <param name="maximumEntityCount">The maximum number of entities.</param>
    /// <param name="maximumRelationshipCount">The maximum number of relationships.</param>
    /// <param name="cancellationToken">A token that cancels row projection.</param>
    /// <returns>The bounded graph viewport.</returns>
    internal static GraphViewport ReadViewport(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        GraphEntityKey? requestedCenter,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelationshipCount);
        GraphStateSummary state = ReadState(connection, snapshot);
        Guid generationId = state.GenerationId;
        GraphEntitySummary? center = requestedCenter is GraphEntityKey entity
            ? ReadEntitySummary(connection, generationId, entity)
            : ReadDefaultCenter(connection, generationId);

        if (center is null)
        {
            return new GraphViewport(null, [], [], false);
        }

        if (requestedCenter is null)
        {
            return ReadOverview(
                connection,
                state,
                center,
                maximumEntityCount,
                maximumRelationshipCount,
                cancellationToken);
        }

        Dictionary<GraphEntityKey, GraphEntitySummary> entities = new()
        {
            [center.Entity] = center,
        };
        List<GraphRelationshipKey> relationships = [];
        bool isTruncated = false;
        using SqliteCommand command = CreateViewportRelationshipCommand(
            connection,
            generationId,
            center.Entity,
            maximumRelationshipCount + 1);
        using SqliteDataReader reader = command.ExecuteReader();
        int rowsRead = 0;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowsRead++;

            if (rowsRead > maximumRelationshipCount)
            {
                isTruncated = true;
                break;
            }

            GraphEntitySummary source = ReadEntitySummary(reader, 0);
            GraphEntitySummary target = ReadEntitySummary(reader, 8);
            int entitiesNeeded = (entities.ContainsKey(source.Entity) ? 0 : 1)
                + (entities.ContainsKey(target.Entity) ? 0 : 1);
            if (entities.Count + entitiesNeeded > maximumEntityCount)
            {
                isTruncated = true;
                continue;
            }

            entities.TryAdd(source.Entity, source);
            entities.TryAdd(target.Entity, target);
            relationships.Add(new GraphRelationshipKey(
                source.Entity,
                target.Entity,
                reader.GetString(16),
                reader.GetString(17)));
        }

        return new GraphViewport(center.Entity, entities.Values, relationships, isTruncated);
    }

    /// <summary>
    /// Reads a bounded undirected neighborhood around one or more active-generation entities.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="requestedCenters">The requested neighborhood centers.</param>
    /// <param name="maximumDepth">The maximum relationship distance from a center.</param>
    /// <param name="maximumEntityCount">The maximum number of entities.</param>
    /// <param name="maximumRelationshipCount">The maximum number of relationships.</param>
    /// <param name="cancellationToken">A token that cancels traversal.</param>
    /// <returns>The bounded active-generation neighborhood.</returns>
    internal static GraphViewport ReadNeighborhood(
        SqliteConnection connection,
        IReadOnlyCollection<GraphEntityKey> requestedCenters,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        return ReadNeighborhood(
            connection,
            GetActiveSnapshot(connection),
            requestedCenters,
            maximumDepth,
            maximumEntityCount,
            maximumRelationshipCount,
            cancellationToken);
    }

    /// <summary>
    /// Reads a bounded merged neighborhood from one graph snapshot.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The graph generation to traverse.</param>
    /// <param name="requestedCenters">The requested neighborhood centers.</param>
    /// <param name="maximumDepth">The maximum relationship distance from a center.</param>
    /// <param name="maximumEntityCount">The maximum number of entities.</param>
    /// <param name="maximumRelationshipCount">The maximum number of relationships.</param>
    /// <param name="cancellationToken">A token that cancels traversal.</param>
    /// <returns>The bounded merged graph neighborhood.</returns>
    internal static GraphViewport ReadNeighborhood(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        IReadOnlyCollection<GraphEntityKey> requestedCenters,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(requestedCenters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelationshipCount);
        GraphSqliteCatalogReader.ValidateSnapshot(connection, snapshot);
        Guid generationId = snapshot.GenerationId;
        GraphEntityKey[] distinctCenters = requestedCenters.Distinct().ToArray();
        HashSet<GraphEntityKey> selectedEntities = [];
        List<GraphEntityKey> activeCenters = [];
        bool isTruncated = false;

        foreach (GraphEntityKey requestedCenter in distinctCenters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphEntitySummary? summary = ReadEntitySummary(connection, generationId, requestedCenter);

            if (summary is not null)
            {
                if (selectedEntities.Count < maximumEntityCount)
                {
                    selectedEntities.Add(summary.Entity);
                    activeCenters.Add(summary.Entity);
                }
                else
                {
                    isTruncated = true;
                }
            }
        }

        if (activeCenters.Count == 0)
        {
            return new GraphViewport(null, [], [], isTruncated);
        }

        CreateNeighborhoodTables(connection);

        try
        {
            HashSet<GraphEntityKey> frontier = activeCenters.ToHashSet();

            for (int depth = 0; depth < maximumDepth && frontier.Count > 0; depth++)
            {
                ReplaceTemporaryEntities(connection, true, frontier);
                HashSet<GraphEntityKey> nextFrontier = [];
                using SqliteCommand command = CreateNeighborhoodExpansionCommand(connection, generationId);
                using SqliteDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    GraphEntityKey source = ReadEntityKey(reader, 0);
                    GraphEntityKey target = ReadEntityKey(reader, 4);
                    AddDiscoveredEntity(
                        source,
                        selectedEntities,
                        nextFrontier,
                        maximumEntityCount,
                        ref isTruncated);
                    AddDiscoveredEntity(
                        target,
                        selectedEntities,
                        nextFrontier,
                        maximumEntityCount,
                        ref isTruncated);
                }

                frontier = nextFrontier;
            }

            ReplaceTemporaryEntities(connection, false, selectedEntities);
            Dictionary<GraphEntityKey, GraphEntitySummary> entities = ReadNeighborhoodEntities(
                connection,
                generationId,
                cancellationToken);
            List<GraphRelationshipKey> relationships = ReadNeighborhoodRelationships(
                connection,
                generationId,
                maximumRelationshipCount,
                cancellationToken,
                ref isTruncated);
            return new GraphViewport(activeCenters[0], entities.Values, relationships, isTruncated);
        }
        finally
        {
            DropNeighborhoodTables(connection);
        }
    }

    /// <summary>
    /// Reads the bounded union of shortest undirected routes between two entities.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The graph generation to traverse.</param>
    /// <param name="start">The route start entity.</param>
    /// <param name="destination">The route destination entity.</param>
    /// <param name="maximumEntityCount">The maximum number of route entities.</param>
    /// <param name="maximumRelationshipCount">The maximum number of route relationships.</param>
    /// <param name="cancellationToken">A token that cancels traversal.</param>
    /// <returns>Connectivity and a bounded route viewport.</returns>
    internal static GraphRouteResult ReadRoutes(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        GraphEntityKey start,
        GraphEntityKey destination,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntityCount, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelationshipCount);
        GraphSqliteCatalogReader.ValidateSnapshot(connection, snapshot);
        Guid generationId = snapshot.GenerationId;
        GraphEntitySummary startSummary = ReadEntitySummary(connection, generationId, start)
            ?? throw new InvalidOperationException("The route start does not exist in the selected graph.");
        GraphEntitySummary destinationSummary = ReadEntitySummary(connection, generationId, destination)
            ?? throw new InvalidOperationException("The route destination does not exist in the selected graph.");

        if (start == destination)
        {
            GraphViewport sameEntityViewport = new(start, [startSummary], [], false);
            return new GraphRouteResult(start, destination, 0, sameEntityViewport);
        }

        List<GraphRelationshipKey> relationships = ReadRouteRelationships(
            connection,
            generationId,
            cancellationToken);
        Dictionary<GraphEntityKey, List<GraphRelationshipKey>> adjacency = BuildRouteAdjacency(
            relationships,
            cancellationToken);
        (Dictionary<GraphEntityKey, int> distances,
            Dictionary<GraphEntityKey, List<GraphRelationshipKey>> predecessors) = FindShortestRoutePredecessors(
                adjacency,
                start,
                destination,
                cancellationToken);

        if (!distances.TryGetValue(destination, out int shortestHopCount))
        {
            GraphViewport disconnectedViewport = new(
                start,
                [startSummary, destinationSummary],
                [],
                false);
            return new GraphRouteResult(start, destination, null, disconnectedViewport);
        }

        (HashSet<GraphEntityKey> routeEntities,
            HashSet<GraphRelationshipKey> routeRelationships) = BacktrackShortestRoutes(
                predecessors,
                distances,
                start,
                destination,
                cancellationToken);
        bool isTruncated = routeEntities.Count > maximumEntityCount
            || routeRelationships.Count > maximumRelationshipCount;

        if (isTruncated)
        {
            (routeEntities, routeRelationships) = BacktrackOneShortestRoute(
                predecessors,
                distances,
                start,
                destination,
                cancellationToken);

            if (routeEntities.Count > maximumEntityCount
                || routeRelationships.Count > maximumRelationshipCount)
            {
                throw new InvalidOperationException(
                    $"The shortest route has {shortestHopCount} hops and exceeds the graph viewport limits.");
            }
        }

        GraphViewport viewport = ReadRouteViewport(
            connection,
            generationId,
            start,
            routeEntities,
            relationships.Where(routeRelationships.Contains).ToArray(),
            isTruncated,
            cancellationToken);
        return new GraphRouteResult(start, destination, shortestHopCount, viewport);
    }

    /// <summary>
    /// Finds staged identities that match differently typed entities in one graph snapshot.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The target graph snapshot.</param>
    /// <param name="candidates">The staged identity candidates.</param>
    /// <param name="cancellationToken">A token that cancels candidate staging and matching.</param>
    /// <returns>The possible duplicate identity conflicts.</returns>
    internal static IReadOnlyList<GraphEntityIdentityConflict> FindIdentityConflicts(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        IEnumerable<GraphEntityIdentityCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(candidates);
        GraphSqliteCatalogReader.ValidateSnapshot(connection, snapshot);
        using (SqliteCommand createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = """
                DROP TABLE IF EXISTS temp.graph_identity_candidates;
                CREATE TEMP TABLE graph_identity_candidates (
                    kind INTEGER NOT NULL,
                    type_name TEXT NOT NULL,
                    canonical_id TEXT NOT NULL,
                    source_namespace TEXT NOT NULL,
                    display_label TEXT NOT NULL,
                    occurrence_count INTEGER NOT NULL,
                    PRIMARY KEY (kind, type_name, canonical_id, source_namespace)
                ) WITHOUT ROWID;
                """;
            createCommand.ExecuteNonQuery();
        }

        using (SqliteCommand insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText = """
                INSERT INTO graph_identity_candidates (
                    kind,
                    type_name,
                    canonical_id,
                    source_namespace,
                    display_label,
                    occurrence_count
                ) VALUES (
                    $kind,
                    $typeName,
                    $canonicalId,
                    $sourceNamespace,
                    $displayLabel,
                    $occurrenceCount
                )
                ON CONFLICT (kind, type_name, canonical_id, source_namespace) DO UPDATE SET
                    occurrence_count = occurrence_count + excluded.occurrence_count;
                """;
            insertCommand.Parameters.Add("$kind", SqliteType.Integer);
            insertCommand.Parameters.Add("$typeName", SqliteType.Text);
            insertCommand.Parameters.Add("$canonicalId", SqliteType.Text);
            insertCommand.Parameters.Add("$sourceNamespace", SqliteType.Text);
            insertCommand.Parameters.Add("$displayLabel", SqliteType.Text);
            insertCommand.Parameters.Add("$occurrenceCount", SqliteType.Integer);

            foreach (GraphEntityIdentityCandidate candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                insertCommand.Parameters["$kind"].Value = (int)candidate.Entity.Kind;
                insertCommand.Parameters["$typeName"].Value = candidate.Entity.TypeName;
                insertCommand.Parameters["$canonicalId"].Value = candidate.Entity.CanonicalId;
                insertCommand.Parameters["$sourceNamespace"].Value = candidate.Entity.SourceNamespace;
                insertCommand.Parameters["$displayLabel"].Value = candidate.DisplayLabel;
                insertCommand.Parameters["$occurrenceCount"].Value = candidate.OccurrenceCount;
                insertCommand.ExecuteNonQuery();
            }
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                candidate.kind,
                candidate.type_name,
                candidate.canonical_id,
                candidate.source_namespace,
                candidate.display_label,
                candidate.occurrence_count,
                existing.kind,
                existing.type_name,
                existing.canonical_id,
                existing.source_namespace,
                existing.display_label,
                CASE
                    WHEN LOWER(TRIM(candidate.canonical_id)) = LOWER(TRIM(existing.canonical_id)) THEN 0
                    ELSE 1
                END AS match_kind
            FROM graph_identity_candidates AS candidate
            INNER JOIN graph_entities AS existing
                ON existing.generation_id = $generationId
                AND existing.source_namespace = candidate.source_namespace
                AND (
                    LOWER(TRIM(existing.canonical_id)) = LOWER(TRIM(candidate.canonical_id))
                    OR LOWER(TRIM(existing.display_label)) = LOWER(TRIM(candidate.display_label))
                )
            WHERE NOT (
                existing.kind = candidate.kind
                AND existing.type_name = candidate.type_name
                AND existing.canonical_id = candidate.canonical_id
                AND existing.source_namespace = candidate.source_namespace
            )
            ORDER BY
                candidate.type_name,
                candidate.canonical_id,
                match_kind,
                existing.type_name,
                existing.canonical_id;
            """;
        command.Parameters.AddWithValue(
            "$generationId",
            snapshot.GenerationId.ToString("D", CultureInfo.InvariantCulture));
        using SqliteDataReader reader = command.ExecuteReader();
        Dictionary<GraphEntityKey, List<GraphEntityIdentityCandidate>> candidateGroups = [];
        Dictionary<GraphEntityKey, (GraphEntityIdentityMatchKind Kind, string Value)> matches = [];

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphEntityKey incomingEntity = new(
                (GraphEntityKind)reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3));
            if (!candidateGroups.TryGetValue(incomingEntity, out List<GraphEntityIdentityCandidate>? group))
            {
                group =
                [
                    new GraphEntityIdentityCandidate(
                        incomingEntity,
                        reader.GetString(4),
                        reader.GetInt32(5),
                        false),
                ];
                candidateGroups.Add(incomingEntity, group);
                GraphEntityIdentityMatchKind matchKind = (GraphEntityIdentityMatchKind)reader.GetInt32(11);
                string matchValue = matchKind == GraphEntityIdentityMatchKind.CanonicalId
                    ? incomingEntity.CanonicalId
                    : reader.GetString(4);
                matches.Add(incomingEntity, (matchKind, matchValue));
            }

            GraphEntityIdentityCandidate existingCandidate = new(
                new GraphEntityKey(
                    (GraphEntityKind)reader.GetInt32(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9)),
                reader.GetString(10),
                1,
                true);
            if (!group.Any(candidate => candidate.Entity == existingCandidate.Entity))
            {
                group.Add(existingCandidate);
            }
        }

        return candidateGroups
            .Select(group =>
            {
                (GraphEntityIdentityMatchKind matchKind, string matchValue) = matches[group.Key];
                GraphEntityIdentityCandidate suggested = group.Value
                    .Where(candidate => candidate.IsExisting)
                    .OrderBy(candidate => candidate.Entity.Kind == GraphEntityKind.Unknown)
                    .ThenBy(candidate => !string.Equals(
                        candidate.Entity.TypeName,
                        candidate.Entity.Kind.ToString(),
                        StringComparison.OrdinalIgnoreCase))
                    .ThenBy(candidate => candidate.Entity.TypeName, StringComparer.OrdinalIgnoreCase)
                    .First();
                return new GraphEntityIdentityConflict(
                    matchKind,
                    matchValue,
                    group.Value,
                    suggested.Entity);
            })
            .ToArray();
    }

    /// <summary>
    /// Searches the active graph's entity full-text index and returns count-only summaries.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="searchText">The analyst-entered search text.</param>
    /// <param name="maximumResults">The maximum number of summaries.</param>
    /// <returns>Matching entity summaries in relevance order.</returns>
    internal static IReadOnlyList<GraphEntitySummary> SearchEntities(
        SqliteConnection connection,
        string searchText,
        int maximumResults)
    {
        return SearchEntities(connection, GetActiveSnapshot(connection), searchText, maximumResults);
    }

    /// <summary>
    /// Searches one current graph snapshot for matching entity summaries.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The graph generation to search.</param>
    /// <param name="searchText">The analyst-entered search text.</param>
    /// <param name="maximumResults">The maximum number of summaries.</param>
    /// <returns>Matching entity summaries in relevance order.</returns>
    internal static IReadOnlyList<GraphEntitySummary> SearchEntities(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        string searchText,
        int maximumResults)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
        GraphSqliteCatalogReader.ValidateSnapshot(connection, snapshot);
        Guid generationId = snapshot.GenerationId;
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                entity.kind,
                entity.type_name,
                entity.canonical_id,
                entity.source_namespace,
                entity.display_label,
                entity.first_discovered_at_utc,
                entity.last_updated_at_utc,
                (
                    SELECT COUNT(*)
                    FROM graph_relationships relationship
                    WHERE relationship.generation_id = entity.generation_id
                        AND (
                            (
                                relationship.source_kind = entity.kind
                                AND relationship.source_type_name = entity.type_name
                                AND relationship.source_canonical_id = entity.canonical_id
                                AND relationship.source_namespace = entity.source_namespace
                            )
                            OR (
                                relationship.target_kind = entity.kind
                                AND relationship.target_type_name = entity.type_name
                                AND relationship.target_canonical_id = entity.canonical_id
                                AND relationship.target_namespace = entity.source_namespace
                            )
                        )
                ) AS degree
            FROM graph_entities entity
            WHERE entity.generation_id = $generationId
                AND (
                    LOWER(entity.display_label) LIKE $containsPattern ESCAPE '\'
                    OR LOWER(entity.canonical_id) LIKE $containsPattern ESCAPE '\'
                    OR LOWER(entity.type_name) LIKE $containsPattern ESCAPE '\'
                    OR LOWER(entity.source_namespace) LIKE $containsPattern ESCAPE '\'
                )
            ORDER BY
                CASE
                    WHEN LOWER(entity.display_label) = $searchValue
                        OR LOWER(entity.canonical_id) = $searchValue THEN 0
                    WHEN LOWER(entity.display_label) LIKE $prefixPattern ESCAPE '\'
                        OR LOWER(entity.canonical_id) LIKE $prefixPattern ESCAPE '\' THEN 1
                    ELSE 2
                END,
                entity.display_label COLLATE NOCASE,
                entity.canonical_id COLLATE NOCASE
            LIMIT $maximumResults;
            """;
        string searchValue = searchText.Trim().ToLowerInvariant();
        string escapedSearchValue = EscapeLikePattern(searchValue);
        command.Parameters.AddWithValue("$searchValue", searchValue);
        command.Parameters.AddWithValue("$prefixPattern", $"{escapedSearchValue}%");
        command.Parameters.AddWithValue("$containsPattern", $"%{escapedSearchValue}%");
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$maximumResults", maximumResults);
        using SqliteDataReader reader = command.ExecuteReader();
        List<GraphEntitySummary> results = [];

        while (reader.Read())
        {
            GraphEntityKey entity = new(
                (GraphEntityKind)reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3));
            results.Add(new GraphEntitySummary(
                entity,
                reader.GetString(4),
                ParseTimestamp(reader.GetString(5)),
                ParseTimestamp(reader.GetString(6)),
                reader.GetInt64(7)));
        }

        return results.AsReadOnly();
    }

    private static void AddDiscoveredEntity(
        GraphEntityKey entity,
        HashSet<GraphEntityKey> selectedEntities,
        HashSet<GraphEntityKey> nextFrontier,
        int maximumEntityCount,
        ref bool isTruncated)
    {
        if (!selectedEntities.Contains(entity))
        {
            if (selectedEntities.Count < maximumEntityCount)
            {
                selectedEntities.Add(entity);
                nextFrontier.Add(entity);
            }
            else
            {
                isTruncated = true;
            }
        }
    }

    private static void AddRouteAdjacency(
        Dictionary<GraphEntityKey, List<GraphRelationshipKey>> adjacency,
        GraphEntityKey entity,
        GraphRelationshipKey relationship)
    {
        if (!adjacency.TryGetValue(entity, out List<GraphRelationshipKey>? adjacentRelationships))
        {
            adjacentRelationships = [];
            adjacency.Add(entity, adjacentRelationships);
        }

        adjacentRelationships.Add(relationship);
    }

    private static (HashSet<GraphEntityKey> Entities, HashSet<GraphRelationshipKey> Relationships)
        BacktrackOneShortestRoute(
            Dictionary<GraphEntityKey, List<GraphRelationshipKey>> predecessors,
            Dictionary<GraphEntityKey, int> distances,
            GraphEntityKey start,
            GraphEntityKey destination,
            CancellationToken cancellationToken)
    {
        HashSet<GraphEntityKey> entities = [destination];
        HashSet<GraphRelationshipKey> relationships = [];
        GraphEntityKey current = destination;

        while (current != start)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphRelationshipKey relationship = predecessors[current][0];
            GraphEntityKey predecessor = GetOtherEndpoint(relationship, current);

            if (distances[predecessor] != distances[current] - 1)
            {
                throw new InvalidOperationException("The shortest route predecessor chain is invalid.");
            }

            relationships.Add(relationship);
            entities.Add(predecessor);
            current = predecessor;
        }

        return (entities, relationships);
    }

    private static (HashSet<GraphEntityKey> Entities, HashSet<GraphRelationshipKey> Relationships)
        BacktrackShortestRoutes(
            Dictionary<GraphEntityKey, List<GraphRelationshipKey>> predecessors,
            Dictionary<GraphEntityKey, int> distances,
            GraphEntityKey start,
            GraphEntityKey destination,
            CancellationToken cancellationToken)
    {
        HashSet<GraphEntityKey> entities = [destination];
        HashSet<GraphRelationshipKey> relationships = [];
        Stack<GraphEntityKey> pending = new();
        pending.Push(destination);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphEntityKey current = pending.Pop();

            if (current == start)
            {
                continue;
            }

            AddShortestRoutePredecessors(
                current,
                predecessors[current],
                distances,
                entities,
                relationships,
                pending);
        }

        return (entities, relationships);
    }

    private static void AddShortestRoutePredecessors(
        GraphEntityKey current,
        IEnumerable<GraphRelationshipKey> predecessors,
        Dictionary<GraphEntityKey, int> distances,
        HashSet<GraphEntityKey> entities,
        HashSet<GraphRelationshipKey> relationships,
        Stack<GraphEntityKey> pending)
    {
        foreach (GraphRelationshipKey relationship in predecessors)
        {
            GraphEntityKey predecessor = GetOtherEndpoint(relationship, current);

            if (distances[predecessor] != distances[current] - 1)
            {
                continue;
            }

            relationships.Add(relationship);

            if (entities.Add(predecessor))
            {
                pending.Push(predecessor);
            }
        }
    }

    private static bool AddShortestRouteNeighbor(
        GraphEntityKey neighbor,
        int candidateDistance,
        GraphRelationshipKey relationship,
        Dictionary<GraphEntityKey, int> distances,
        Dictionary<GraphEntityKey, List<GraphRelationshipKey>> predecessors,
        Queue<GraphEntityKey> pending)
    {
        if (distances.TryGetValue(neighbor, out int knownDistance))
        {
            if (knownDistance == candidateDistance)
            {
                predecessors[neighbor].Add(relationship);
            }

            return false;
        }

        distances.Add(neighbor, candidateDistance);
        predecessors.Add(neighbor, [relationship]);
        pending.Enqueue(neighbor);
        return true;
    }

    private static Dictionary<GraphEntityKey, List<GraphRelationshipKey>> BuildRouteAdjacency(
        IEnumerable<GraphRelationshipKey> relationships,
        CancellationToken cancellationToken)
    {
        Dictionary<GraphEntityKey, List<GraphRelationshipKey>> adjacency = [];

        foreach (GraphRelationshipKey relationship in relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddRouteAdjacency(adjacency, relationship.Source, relationship);

            if (relationship.Target != relationship.Source)
            {
                AddRouteAdjacency(adjacency, relationship.Target, relationship);
            }
        }

        return adjacency;
    }

    private static (Dictionary<GraphEntityKey, int> Distances,
        Dictionary<GraphEntityKey, List<GraphRelationshipKey>> Predecessors) FindShortestRoutePredecessors(
            Dictionary<GraphEntityKey, List<GraphRelationshipKey>> adjacency,
            GraphEntityKey start,
            GraphEntityKey destination,
            CancellationToken cancellationToken)
    {
        Dictionary<GraphEntityKey, int> distances = new() { [start] = 0 };
        Dictionary<GraphEntityKey, List<GraphRelationshipKey>> predecessors = [];
        Queue<GraphEntityKey> pending = new();
        pending.Enqueue(start);
        int? shortestDistance = null;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphEntityKey current = pending.Dequeue();
            int currentDistance = distances[current];

            if (shortestDistance is int completedDistance && currentDistance >= completedDistance)
            {
                continue;
            }

            if (!adjacency.TryGetValue(current, out List<GraphRelationshipKey>? adjacentRelationships))
            {
                continue;
            }

            foreach (GraphRelationshipKey relationship in adjacentRelationships)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GraphEntityKey neighbor = GetOtherEndpoint(relationship, current);
                int candidateDistance = currentDistance + 1;

                if (AddShortestRouteNeighbor(
                    neighbor,
                    candidateDistance,
                    relationship,
                    distances,
                    predecessors,
                    pending) && neighbor == destination)
                {
                    shortestDistance = candidateDistance;
                }
            }
        }

        return (distances, predecessors);
    }

    private static GraphEntityKey GetOtherEndpoint(
        GraphRelationshipKey relationship,
        GraphEntityKey entity)
    {
        return relationship.Source == entity ? relationship.Target : relationship.Source;
    }

    private static List<GraphRelationshipKey> ReadRouteRelationships(
        SqliteConnection connection,
        Guid generationId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                source_kind,
                source_type_name,
                source_canonical_id,
                source_namespace,
                target_kind,
                target_type_name,
                target_canonical_id,
                target_namespace,
                relationship_type,
                discriminator
            FROM graph_relationships
            WHERE generation_id = $generationId
            ORDER BY
                source_kind,
                source_type_name,
                source_canonical_id,
                source_namespace,
                target_kind,
                target_type_name,
                target_canonical_id,
                target_namespace,
                relationship_type,
                discriminator;
            """;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        using SqliteDataReader reader = command.ExecuteReader();
        List<GraphRelationshipKey> relationships = [];

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            relationships.Add(new GraphRelationshipKey(
                ReadEntityKey(reader, 0),
                ReadEntityKey(reader, 4),
                reader.GetString(8),
                reader.GetString(9)));
        }

        return relationships;
    }

    private static GraphViewport ReadRouteViewport(
        SqliteConnection connection,
        Guid generationId,
        GraphEntityKey start,
        IReadOnlyCollection<GraphEntityKey> routeEntities,
        IReadOnlyCollection<GraphRelationshipKey> routeRelationships,
        bool isTruncated,
        CancellationToken cancellationToken)
    {
        CreateNeighborhoodTables(connection);

        try
        {
            ReplaceTemporaryEntities(connection, false, routeEntities);
            Dictionary<GraphEntityKey, GraphEntitySummary> entities = ReadNeighborhoodEntities(
                connection,
                generationId,
                cancellationToken);
            return new GraphViewport(start, entities.Values, routeRelationships, isTruncated);
        }
        finally
        {
            DropNeighborhoodTables(connection);
        }
    }

    private static SqliteCommand CreateNeighborhoodExpansionCommand(
        SqliteConnection connection,
        Guid generationId)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT
                relationship.source_kind,
                relationship.source_type_name,
                relationship.source_canonical_id,
                relationship.source_namespace,
                relationship.target_kind,
                relationship.target_type_name,
                relationship.target_canonical_id,
                relationship.target_namespace
            FROM graph_relationships relationship
            INNER JOIN graph_neighborhood_frontier frontier
                ON (
                    frontier.kind = relationship.source_kind
                    AND frontier.type_name = relationship.source_type_name
                    AND frontier.canonical_id = relationship.source_canonical_id
                    AND frontier.source_namespace = relationship.source_namespace
                )
                OR (
                    frontier.kind = relationship.target_kind
                    AND frontier.type_name = relationship.target_type_name
                    AND frontier.canonical_id = relationship.target_canonical_id
                    AND frontier.source_namespace = relationship.target_namespace
                )
            WHERE relationship.generation_id = $generationId;
            """;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        return command;
    }

    private static void CreateNeighborhoodTables(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE IF EXISTS graph_neighborhood_frontier;
            DROP TABLE IF EXISTS graph_neighborhood_selected;
            CREATE TEMP TABLE graph_neighborhood_frontier (
                kind INTEGER NOT NULL,
                type_name TEXT NOT NULL,
                canonical_id TEXT NOT NULL,
                source_namespace TEXT NOT NULL,
                PRIMARY KEY (kind, type_name, canonical_id, source_namespace)
            ) WITHOUT ROWID;
            CREATE TEMP TABLE graph_neighborhood_selected (
                kind INTEGER NOT NULL,
                type_name TEXT NOT NULL,
                canonical_id TEXT NOT NULL,
                source_namespace TEXT NOT NULL,
                PRIMARY KEY (kind, type_name, canonical_id, source_namespace)
            ) WITHOUT ROWID;
            """;
        command.ExecuteNonQuery();
    }

    private static void DropNeighborhoodTables(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE IF EXISTS graph_neighborhood_frontier;
            DROP TABLE IF EXISTS graph_neighborhood_selected;
            """;
        command.ExecuteNonQuery();
    }

    private static Dictionary<GraphEntityKey, GraphEntitySummary> ReadNeighborhoodEntities(
        SqliteConnection connection,
        Guid generationId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH endpoints AS (
                SELECT
                    source_kind AS kind,
                    source_type_name AS type_name,
                    source_canonical_id AS canonical_id,
                    source_namespace AS source_namespace
                FROM graph_relationships
                WHERE generation_id = $generationId
                UNION ALL
                SELECT
                    target_kind AS kind,
                    target_type_name AS type_name,
                    target_canonical_id AS canonical_id,
                    target_namespace AS source_namespace
                FROM graph_relationships
                WHERE generation_id = $generationId
            ),
            degrees AS (
                SELECT kind, type_name, canonical_id, source_namespace, COUNT(*) AS degree
                FROM endpoints
                GROUP BY kind, type_name, canonical_id, source_namespace
            )
            SELECT
                entity.kind,
                entity.type_name,
                entity.canonical_id,
                entity.source_namespace,
                entity.display_label,
                entity.first_discovered_at_utc,
                entity.last_updated_at_utc,
                COALESCE(degrees.degree, 0)
            FROM graph_entities entity
            INNER JOIN graph_neighborhood_selected selected
                ON selected.kind = entity.kind
                AND selected.type_name = entity.type_name
                AND selected.canonical_id = entity.canonical_id
                AND selected.source_namespace = entity.source_namespace
            LEFT JOIN degrees
                ON degrees.kind = entity.kind
                AND degrees.type_name = entity.type_name
                AND degrees.canonical_id = entity.canonical_id
                AND degrees.source_namespace = entity.source_namespace
            WHERE entity.generation_id = $generationId
            ORDER BY entity.display_label COLLATE NOCASE, entity.canonical_id COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        using SqliteDataReader reader = command.ExecuteReader();
        Dictionary<GraphEntityKey, GraphEntitySummary> entities = [];

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphEntitySummary entity = ReadEntitySummary(reader, 0);
            entities.Add(entity.Entity, entity);
        }

        return entities;
    }

    private static List<GraphRelationshipKey> ReadNeighborhoodRelationships(
        SqliteConnection connection,
        Guid generationId,
        int maximumRelationshipCount,
        CancellationToken cancellationToken,
        ref bool isTruncated)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                relationship.source_kind,
                relationship.source_type_name,
                relationship.source_canonical_id,
                relationship.source_namespace,
                relationship.target_kind,
                relationship.target_type_name,
                relationship.target_canonical_id,
                relationship.target_namespace,
                relationship.relationship_type,
                relationship.discriminator
            FROM graph_relationships relationship
            INNER JOIN graph_neighborhood_selected source
                ON source.kind = relationship.source_kind
                AND source.type_name = relationship.source_type_name
                AND source.canonical_id = relationship.source_canonical_id
                AND source.source_namespace = relationship.source_namespace
            INNER JOIN graph_neighborhood_selected target
                ON target.kind = relationship.target_kind
                AND target.type_name = relationship.target_type_name
                AND target.canonical_id = relationship.target_canonical_id
                AND target.source_namespace = relationship.target_namespace
            WHERE relationship.generation_id = $generationId
            ORDER BY
                relationship.last_updated_at_utc DESC,
                relationship.relationship_type,
                relationship.discriminator
            LIMIT $rowLimit;
            """;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rowLimit", maximumRelationshipCount + 1);
        using SqliteDataReader reader = command.ExecuteReader();
        List<GraphRelationshipKey> relationships = [];

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (relationships.Count == maximumRelationshipCount)
            {
                isTruncated = true;
                break;
            }

            relationships.Add(new GraphRelationshipKey(
                ReadEntityKey(reader, 0),
                ReadEntityKey(reader, 4),
                reader.GetString(8),
                reader.GetString(9)));
        }

        return relationships;
    }

    private static GraphEntityKey ReadEntityKey(SqliteDataReader reader, int offset)
    {
        return new GraphEntityKey(
            (GraphEntityKind)reader.GetInt32(offset),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            reader.GetString(offset + 3));
    }

    private static void ReplaceTemporaryEntities(
        SqliteConnection connection,
        bool replaceFrontier,
        IEnumerable<GraphEntityKey> entities)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = replaceFrontier
                ? "DELETE FROM graph_neighborhood_frontier;"
                : "DELETE FROM graph_neighborhood_selected;";
            deleteCommand.ExecuteNonQuery();
        }

        using SqliteCommand insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = replaceFrontier
            ? """
                INSERT INTO graph_neighborhood_frontier (kind, type_name, canonical_id, source_namespace)
                VALUES ($kind, $typeName, $canonicalId, $sourceNamespace);
                """
            : """
                INSERT INTO graph_neighborhood_selected (kind, type_name, canonical_id, source_namespace)
                VALUES ($kind, $typeName, $canonicalId, $sourceNamespace);
                """;
        insertCommand.Parameters.Add("$kind", SqliteType.Integer);
        insertCommand.Parameters.Add("$typeName", SqliteType.Text);
        insertCommand.Parameters.Add("$canonicalId", SqliteType.Text);
        insertCommand.Parameters.Add("$sourceNamespace", SqliteType.Text);
        insertCommand.Prepare();

        foreach (GraphEntityKey entity in entities)
        {
            insertCommand.Parameters["$kind"].Value = (int)entity.Kind;
            insertCommand.Parameters["$typeName"].Value = entity.TypeName;
            insertCommand.Parameters["$canonicalId"].Value = entity.CanonicalId;
            insertCommand.Parameters["$sourceNamespace"].Value = entity.SourceNamespace;
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static GraphViewport ReadOverview(
        SqliteConnection connection,
        GraphStateSummary state,
        GraphEntitySummary center,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        Dictionary<GraphEntityKey, GraphEntitySummary> entities = [];
        using (SqliteCommand entityCommand = CreateOverviewEntityCommand(
            connection,
            state.GenerationId,
            maximumEntityCount))
        using (SqliteDataReader entityReader = entityCommand.ExecuteReader())
        {
            while (entityReader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                GraphEntitySummary entity = ReadEntitySummary(entityReader, 0);
                entities.Add(entity.Entity, entity);
            }
        }

        List<GraphRelationshipKey> relationships = [];
        using (SqliteCommand relationshipCommand = CreateOverviewRelationshipCommand(
            connection,
            state.GenerationId,
            maximumEntityCount,
            maximumRelationshipCount))
        using (SqliteDataReader relationshipReader = relationshipCommand.ExecuteReader())
        {
            while (relationshipReader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                GraphEntityKey source = new(
                    (GraphEntityKind)relationshipReader.GetInt32(0),
                    relationshipReader.GetString(1),
                    relationshipReader.GetString(2),
                    relationshipReader.GetString(3));
                GraphEntityKey target = new(
                    (GraphEntityKind)relationshipReader.GetInt32(4),
                    relationshipReader.GetString(5),
                    relationshipReader.GetString(6),
                    relationshipReader.GetString(7));
                relationships.Add(new GraphRelationshipKey(
                    source,
                    target,
                    relationshipReader.GetString(8),
                    relationshipReader.GetString(9)));
            }
        }

        bool isTruncated = state.EntityCount > entities.Count
            || state.RelationshipCount > relationships.Count;
        return new GraphViewport(center.Entity, entities.Values, relationships, isTruncated);
    }

    private static SqliteCommand CreateViewportRelationshipCommand(
        SqliteConnection connection,
        Guid generationId,
        GraphEntityKey center,
        int rowLimit)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH endpoints AS (
                SELECT
                    source_kind AS kind,
                    source_type_name AS type_name,
                    source_canonical_id AS canonical_id,
                    source_namespace AS source_namespace
                FROM graph_relationships
                WHERE generation_id = $generationId
                UNION ALL
                SELECT
                    target_kind AS kind,
                    target_type_name AS type_name,
                    target_canonical_id AS canonical_id,
                    target_namespace AS source_namespace
                FROM graph_relationships
                WHERE generation_id = $generationId
            ),
            degrees AS (
                SELECT kind, type_name, canonical_id, source_namespace, COUNT(*) AS degree
                FROM endpoints
                GROUP BY kind, type_name, canonical_id, source_namespace
            )
            SELECT
                source.kind,
                source.type_name,
                source.canonical_id,
                source.source_namespace,
                source.display_label,
                source.first_discovered_at_utc,
                source.last_updated_at_utc,
                COALESCE(source_degree.degree, 0),
                target.kind,
                target.type_name,
                target.canonical_id,
                target.source_namespace,
                target.display_label,
                target.first_discovered_at_utc,
                target.last_updated_at_utc,
                COALESCE(target_degree.degree, 0),
                relationship.relationship_type,
                relationship.discriminator
            FROM graph_relationships relationship
            INNER JOIN graph_entities source
                ON source.generation_id = relationship.generation_id
                AND source.kind = relationship.source_kind
                AND source.type_name = relationship.source_type_name
                AND source.canonical_id = relationship.source_canonical_id
                AND source.source_namespace = relationship.source_namespace
            INNER JOIN graph_entities target
                ON target.generation_id = relationship.generation_id
                AND target.kind = relationship.target_kind
                AND target.type_name = relationship.target_type_name
                AND target.canonical_id = relationship.target_canonical_id
                AND target.source_namespace = relationship.target_namespace
            LEFT JOIN degrees source_degree
                ON source_degree.kind = source.kind
                AND source_degree.type_name = source.type_name
                AND source_degree.canonical_id = source.canonical_id
                AND source_degree.source_namespace = source.source_namespace
            LEFT JOIN degrees target_degree
                ON target_degree.kind = target.kind
                AND target_degree.type_name = target.type_name
                AND target_degree.canonical_id = target.canonical_id
                AND target_degree.source_namespace = target.source_namespace
            WHERE relationship.generation_id = $generationId
                AND (
                    (
                        relationship.source_kind = $centerKind
                        AND relationship.source_type_name = $centerTypeName
                        AND relationship.source_canonical_id = $centerCanonicalId
                        AND relationship.source_namespace = $centerNamespace
                    )
                    OR (
                        relationship.target_kind = $centerKind
                        AND relationship.target_type_name = $centerTypeName
                        AND relationship.target_canonical_id = $centerCanonicalId
                        AND relationship.target_namespace = $centerNamespace
                    )
                )
            ORDER BY relationship.last_updated_at_utc DESC, relationship.relationship_type
            LIMIT $rowLimit;
            """;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$centerKind", (int)center.Kind);
        command.Parameters.AddWithValue("$centerTypeName", center.TypeName);
        command.Parameters.AddWithValue("$centerCanonicalId", center.CanonicalId);
        command.Parameters.AddWithValue("$centerNamespace", center.SourceNamespace);
        command.Parameters.AddWithValue("$rowLimit", rowLimit);
        return command;
    }

    private static SqliteCommand CreateOverviewEntityCommand(
        SqliteConnection connection,
        Guid generationId,
        int maximumEntityCount)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH endpoints AS (
                SELECT
                    source_kind AS kind,
                    source_type_name AS type_name,
                    source_canonical_id AS canonical_id,
                    source_namespace AS source_namespace
                FROM graph_relationships
                WHERE generation_id = $generationId
                UNION ALL
                SELECT
                    target_kind AS kind,
                    target_type_name AS type_name,
                    target_canonical_id AS canonical_id,
                    target_namespace AS source_namespace
                FROM graph_relationships
                WHERE generation_id = $generationId
            ),
            degrees AS (
                SELECT kind, type_name, canonical_id, source_namespace, COUNT(*) AS degree
                FROM endpoints
                GROUP BY kind, type_name, canonical_id, source_namespace
            )
            SELECT
                entity.kind,
                entity.type_name,
                entity.canonical_id,
                entity.source_namespace,
                entity.display_label,
                entity.first_discovered_at_utc,
                entity.last_updated_at_utc,
                COALESCE(degrees.degree, 0)
            FROM graph_entities entity
            LEFT JOIN degrees
                ON degrees.kind = entity.kind
                AND degrees.type_name = entity.type_name
                AND degrees.canonical_id = entity.canonical_id
                AND degrees.source_namespace = entity.source_namespace
            WHERE entity.generation_id = $generationId
            ORDER BY
                COALESCE(degrees.degree, 0) DESC,
                entity.last_updated_at_utc DESC,
                entity.display_label COLLATE NOCASE,
                entity.kind,
                entity.type_name,
                entity.canonical_id,
                entity.source_namespace
            LIMIT $maximumEntityCount;
            """;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$maximumEntityCount", maximumEntityCount);
        return command;
    }

    private static SqliteCommand CreateOverviewRelationshipCommand(
        SqliteConnection connection,
        Guid generationId,
        int maximumEntityCount,
        int maximumRelationshipCount)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH endpoints AS (
                SELECT
                    source_kind AS kind,
                    source_type_name AS type_name,
                    source_canonical_id AS canonical_id,
                    source_namespace AS source_namespace
                FROM graph_relationships
                WHERE generation_id = $generationId
                UNION ALL
                SELECT
                    target_kind AS kind,
                    target_type_name AS type_name,
                    target_canonical_id AS canonical_id,
                    target_namespace AS source_namespace
                FROM graph_relationships
                WHERE generation_id = $generationId
            ),
            degrees AS (
                SELECT kind, type_name, canonical_id, source_namespace, COUNT(*) AS degree
                FROM endpoints
                GROUP BY kind, type_name, canonical_id, source_namespace
            ),
            selected_entities AS (
                SELECT
                    entity.kind,
                    entity.type_name,
                    entity.canonical_id,
                    entity.source_namespace
                FROM graph_entities entity
                LEFT JOIN degrees
                    ON degrees.kind = entity.kind
                    AND degrees.type_name = entity.type_name
                    AND degrees.canonical_id = entity.canonical_id
                    AND degrees.source_namespace = entity.source_namespace
                WHERE entity.generation_id = $generationId
                ORDER BY
                    COALESCE(degrees.degree, 0) DESC,
                    entity.last_updated_at_utc DESC,
                    entity.display_label COLLATE NOCASE,
                    entity.kind,
                    entity.type_name,
                    entity.canonical_id,
                    entity.source_namespace
                LIMIT $maximumEntityCount
            )
            SELECT
                relationship.source_kind,
                relationship.source_type_name,
                relationship.source_canonical_id,
                relationship.source_namespace,
                relationship.target_kind,
                relationship.target_type_name,
                relationship.target_canonical_id,
                relationship.target_namespace,
                relationship.relationship_type,
                relationship.discriminator
            FROM graph_relationships relationship
            INNER JOIN selected_entities source
                ON source.kind = relationship.source_kind
                AND source.type_name = relationship.source_type_name
                AND source.canonical_id = relationship.source_canonical_id
                AND source.source_namespace = relationship.source_namespace
            INNER JOIN selected_entities target
                ON target.kind = relationship.target_kind
                AND target.type_name = relationship.target_type_name
                AND target.canonical_id = relationship.target_canonical_id
                AND target.source_namespace = relationship.target_namespace
            WHERE relationship.generation_id = $generationId
            ORDER BY
                relationship.last_updated_at_utc DESC,
                relationship.relationship_type,
                relationship.discriminator
            LIMIT $maximumRelationshipCount;
            """;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$maximumEntityCount", maximumEntityCount);
        command.Parameters.AddWithValue("$maximumRelationshipCount", maximumRelationshipCount);
        return command;
    }

    private static GraphEntitySummary? ReadDefaultCenter(SqliteConnection connection, Guid generationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                entity.kind,
                entity.type_name,
                entity.canonical_id,
                entity.source_namespace,
                entity.display_label,
                entity.first_discovered_at_utc,
                entity.last_updated_at_utc,
                (
                    SELECT COUNT(*)
                    FROM graph_relationships relationship
                    WHERE relationship.generation_id = entity.generation_id
                        AND (
                            (
                                relationship.source_kind = entity.kind
                                AND relationship.source_type_name = entity.type_name
                                AND relationship.source_canonical_id = entity.canonical_id
                                AND relationship.source_namespace = entity.source_namespace
                            )
                            OR (
                                relationship.target_kind = entity.kind
                                AND relationship.target_type_name = entity.type_name
                                AND relationship.target_canonical_id = entity.canonical_id
                                AND relationship.target_namespace = entity.source_namespace
                            )
                        )
                ) AS degree
            FROM graph_entities entity
            WHERE entity.generation_id = $generationId
            ORDER BY degree DESC, entity.last_updated_at_utc DESC, entity.display_label COLLATE NOCASE
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadEntitySummary(reader, 0) : null;
    }

    private static GraphEntitySummary? ReadEntitySummary(
        SqliteConnection connection,
        Guid generationId,
        GraphEntityKey entity)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                entity.kind,
                entity.type_name,
                entity.canonical_id,
                entity.source_namespace,
                entity.display_label,
                entity.first_discovered_at_utc,
                entity.last_updated_at_utc,
                (
                    SELECT COUNT(*)
                    FROM graph_relationships relationship
                    WHERE relationship.generation_id = entity.generation_id
                        AND (
                            (
                                relationship.source_kind = entity.kind
                                AND relationship.source_type_name = entity.type_name
                                AND relationship.source_canonical_id = entity.canonical_id
                                AND relationship.source_namespace = entity.source_namespace
                            )
                            OR (
                                relationship.target_kind = entity.kind
                                AND relationship.target_type_name = entity.type_name
                                AND relationship.target_canonical_id = entity.canonical_id
                                AND relationship.target_namespace = entity.source_namespace
                            )
                        )
                ) AS degree
            FROM graph_entities entity
            WHERE entity.generation_id = $generationId
                AND entity.kind = $kind
                AND entity.type_name = $typeName
                AND entity.canonical_id = $canonicalId
                AND entity.source_namespace = $sourceNamespace;
            """;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", (int)entity.Kind);
        command.Parameters.AddWithValue("$typeName", entity.TypeName);
        command.Parameters.AddWithValue("$canonicalId", entity.CanonicalId);
        command.Parameters.AddWithValue("$sourceNamespace", entity.SourceNamespace);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadEntitySummary(reader, 0) : null;
    }

    private static GraphEntitySummary ReadEntitySummary(SqliteDataReader reader, int offset)
    {
        GraphEntityKey entity = new(
            (GraphEntityKind)reader.GetInt32(offset),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            reader.GetString(offset + 3));
        return new GraphEntitySummary(
            entity,
            reader.GetString(offset + 4),
            ParseTimestamp(reader.GetString(offset + 5)),
            ParseTimestamp(reader.GetString(offset + 6)),
            reader.GetInt64(offset + 7));
    }

    private static void ValidateRetainedTimelinePoint(
        SqliteConnection connection,
        GraphTimelinePoint point)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = point.IngestionId is Guid
            ? """
                SELECT COUNT(*)
                FROM graph_ingestions ingestion
                INNER JOIN graph_generations generation
                    ON generation.id = ingestion.generation_id
                WHERE generation.graph_id = $graphId
                    AND generation.id = $generationId
                    AND ingestion.id = $ingestionId
                    AND ingestion.completed_at_utc = $timestampUtc;
                """
            : """
                SELECT COUNT(*)
                FROM graph_generations generation
                WHERE generation.graph_id = $graphId
                    AND generation.id = $generationId
                    AND generation.created_at_utc = $timestampUtc;
                """;
        command.Parameters.AddWithValue(
            "$graphId",
            point.Snapshot.GraphId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$generationId",
            point.Snapshot.GenerationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$timestampUtc",
            point.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));

        if (point.IngestionId is Guid ingestionId)
        {
            command.Parameters.AddWithValue(
                "$ingestionId",
                ingestionId.ToString("D", CultureInfo.InvariantCulture));
        }

        long matchingPoints = (long)(command.ExecuteScalar() ?? 0L);

        if (matchingPoints != 1)
        {
            throw new InvalidOperationException("The selected graph history point is no longer available.");
        }
    }

    private static List<GraphEntitySummary> ReadTimelineEntities(
        SqliteConnection connection,
        GraphTimelinePoint point,
        int maximumEntityCount,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH visible_relationships AS (
                SELECT *
                FROM graph_relationships
                WHERE generation_id = $generationId
                    AND first_discovered_at_utc <= $timestampUtc
            ),
            endpoints AS (
                SELECT
                    source_kind AS kind,
                    source_type_name AS type_name,
                    source_canonical_id AS canonical_id,
                    source_namespace AS source_namespace
                FROM visible_relationships
                UNION ALL
                SELECT
                    target_kind AS kind,
                    target_type_name AS type_name,
                    target_canonical_id AS canonical_id,
                    target_namespace AS source_namespace
                FROM visible_relationships
            ),
            degrees AS (
                SELECT kind, type_name, canonical_id, source_namespace, COUNT(*) AS degree
                FROM endpoints
                GROUP BY kind, type_name, canonical_id, source_namespace
            )
            SELECT
                entity.kind,
                entity.type_name,
                entity.canonical_id,
                entity.source_namespace,
                COALESCE((
                    SELECT observation.display_label
                    FROM graph_entity_observations observation
                    WHERE observation.generation_id = entity.generation_id
                        AND observation.kind = entity.kind
                        AND observation.type_name = entity.type_name
                        AND observation.canonical_id = entity.canonical_id
                        AND observation.source_namespace = entity.source_namespace
                        AND observation.discovered_at_utc <= $timestampUtc
                    ORDER BY observation.discovered_at_utc DESC, observation.id DESC
                    LIMIT 1
                ), entity.canonical_id),
                entity.first_discovered_at_utc,
                COALESCE((
                    SELECT MAX(observation.discovered_at_utc)
                    FROM graph_entity_observations observation
                    WHERE observation.generation_id = entity.generation_id
                        AND observation.kind = entity.kind
                        AND observation.type_name = entity.type_name
                        AND observation.canonical_id = entity.canonical_id
                        AND observation.source_namespace = entity.source_namespace
                        AND observation.discovered_at_utc <= $timestampUtc
                ), entity.first_discovered_at_utc),
                COALESCE(degrees.degree, 0)
            FROM graph_entities entity
            LEFT JOIN degrees
                ON degrees.kind = entity.kind
                AND degrees.type_name = entity.type_name
                AND degrees.canonical_id = entity.canonical_id
                AND degrees.source_namespace = entity.source_namespace
            WHERE entity.generation_id = $generationId
                AND entity.first_discovered_at_utc <= $timestampUtc
            ORDER BY
                COALESCE(degrees.degree, 0) DESC,
                entity.first_discovered_at_utc DESC,
                entity.canonical_id
            LIMIT $maximumEntityCount;
            """;
        AddTimelineParameters(command, point);
        command.Parameters.AddWithValue("$maximumEntityCount", maximumEntityCount);
        List<GraphEntitySummary> entities = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            entities.Add(ReadEntitySummary(reader, 0));
        }

        return entities;
    }

    private static List<GraphRelationshipKey> ReadTimelineRelationships(
        SqliteConnection connection,
        GraphTimelinePoint point,
        IReadOnlyCollection<GraphEntityKey> selectedEntities,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        ReplaceTimelineSelectedEntities(connection, selectedEntities);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                relationship.source_kind,
                relationship.source_type_name,
                relationship.source_canonical_id,
                relationship.source_namespace,
                relationship.target_kind,
                relationship.target_type_name,
                relationship.target_canonical_id,
                relationship.target_namespace,
                relationship.relationship_type,
                relationship.discriminator
            FROM graph_relationships relationship
            INNER JOIN graph_timeline_selected source
                ON source.kind = relationship.source_kind
                AND source.type_name = relationship.source_type_name
                AND source.canonical_id = relationship.source_canonical_id
                AND source.source_namespace = relationship.source_namespace
            INNER JOIN graph_timeline_selected target
                ON target.kind = relationship.target_kind
                AND target.type_name = relationship.target_type_name
                AND target.canonical_id = relationship.target_canonical_id
                AND target.source_namespace = relationship.target_namespace
            WHERE relationship.generation_id = $generationId
                AND relationship.first_discovered_at_utc <= $timestampUtc
            ORDER BY relationship.first_discovered_at_utc DESC, relationship.relationship_type
            LIMIT $maximumRelationshipCount;
            """;
        AddTimelineParameters(command, point);
        command.Parameters.AddWithValue("$maximumRelationshipCount", maximumRelationshipCount);
        List<GraphRelationshipKey> relationships = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            relationships.Add(new GraphRelationshipKey(
                ReadEntityKey(reader, 0),
                ReadEntityKey(reader, 4),
                reader.GetString(8),
                reader.GetString(9)));
        }

        return relationships;
    }

    private static (long EntityCount, long RelationshipCount) ReadTimelineCounts(
        SqliteConnection connection,
        GraphTimelinePoint point)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM graph_entities entity
                    WHERE entity.generation_id = $generationId
                        AND entity.first_discovered_at_utc <= $timestampUtc),
                (SELECT COUNT(*) FROM graph_relationships relationship
                    WHERE relationship.generation_id = $generationId
                        AND relationship.first_discovered_at_utc <= $timestampUtc);
            """;
        AddTimelineParameters(command, point);
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static void AddTimelineParameters(SqliteCommand command, GraphTimelinePoint point)
    {
        command.Parameters.AddWithValue(
            "$generationId",
            point.Snapshot.GenerationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$timestampUtc",
            point.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static void ReplaceTimelineSelectedEntities(
        SqliteConnection connection,
        IEnumerable<GraphEntityKey> entities)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand setupCommand = connection.CreateCommand())
        {
            setupCommand.Transaction = transaction;
            setupCommand.CommandText = """
                CREATE TEMP TABLE IF NOT EXISTS graph_timeline_selected (
                    kind INTEGER NOT NULL,
                    type_name TEXT NOT NULL,
                    canonical_id TEXT NOT NULL,
                    source_namespace TEXT NOT NULL,
                    PRIMARY KEY (kind, type_name, canonical_id, source_namespace)
                ) WITHOUT ROWID;
                DELETE FROM graph_timeline_selected;
                """;
            setupCommand.ExecuteNonQuery();
        }

        using SqliteCommand insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO graph_timeline_selected (kind, type_name, canonical_id, source_namespace)
            VALUES ($kind, $typeName, $canonicalId, $sourceNamespace);
            """;
        insertCommand.Parameters.Add("$kind", SqliteType.Integer);
        insertCommand.Parameters.Add("$typeName", SqliteType.Text);
        insertCommand.Parameters.Add("$canonicalId", SqliteType.Text);
        insertCommand.Parameters.Add("$sourceNamespace", SqliteType.Text);
        insertCommand.Prepare();

        foreach (GraphEntityKey entity in entities)
        {
            insertCommand.Parameters["$kind"].Value = (int)entity.Kind;
            insertCommand.Parameters["$typeName"].Value = entity.TypeName;
            insertCommand.Parameters["$canonicalId"].Value = entity.CanonicalId;
            insertCommand.Parameters["$sourceNamespace"].Value = entity.SourceNamespace;
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void AddRelationshipParameters(
        SqliteCommand command,
        Guid generationId,
        GraphRelationshipKey relationship)
    {
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sourceKind", (int)relationship.Source.Kind);
        command.Parameters.AddWithValue("$sourceTypeName", relationship.Source.TypeName);
        command.Parameters.AddWithValue("$sourceCanonicalId", relationship.Source.CanonicalId);
        command.Parameters.AddWithValue("$sourceNamespace", relationship.Source.SourceNamespace);
        command.Parameters.AddWithValue("$targetKind", (int)relationship.Target.Kind);
        command.Parameters.AddWithValue("$targetTypeName", relationship.Target.TypeName);
        command.Parameters.AddWithValue("$targetCanonicalId", relationship.Target.CanonicalId);
        command.Parameters.AddWithValue("$targetNamespace", relationship.Target.SourceNamespace);
        command.Parameters.AddWithValue("$relationshipType", relationship.TypeName);
        command.Parameters.AddWithValue("$discriminator", relationship.Discriminator);
    }

    private static ReadOnlyCollection<GraphEvidenceRecord> ReadEntityEvidence(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        GraphEntityKey entity)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                ingestion.id,
                ingestion.source_kind,
                ingestion.source_id,
                ingestion.source_name,
                ingestion.cluster_uri,
                ingestion.database_name,
                ingestion.query_text,
                ingestion.completed_at_utc,
                MAX(observation.discovered_at_utc),
                occurrence.table_name,
                occurrence.row_ordinal,
                payload.schema_json,
                payload.row_json
            FROM graph_entity_observations observation
            INNER JOIN graph_entity_observation_evidence linked
                ON linked.observation_id = observation.id
            INNER JOIN graph_evidence_occurrences occurrence
                ON occurrence.generation_id = linked.generation_id
                AND occurrence.occurrence_id = linked.occurrence_id
            INNER JOIN graph_evidence_payloads payload
                ON payload.content_hash = occurrence.content_hash
            INNER JOIN graph_ingestions ingestion
                ON ingestion.id = occurrence.ingestion_id
            WHERE observation.generation_id = $generationId
                AND observation.kind = $kind
                AND observation.type_name = $typeName
                AND observation.canonical_id = $canonicalId
                AND observation.source_namespace = $sourceNamespace
            GROUP BY occurrence.generation_id, occurrence.occurrence_id
            ORDER BY MAX(observation.discovered_at_utc) DESC, occurrence.occurrence_id DESC
            LIMIT $maximumEvidenceRecords;
            """;
        command.Parameters.AddWithValue(
            "$generationId",
            snapshot.GenerationId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", (int)entity.Kind);
        command.Parameters.AddWithValue("$typeName", entity.TypeName);
        command.Parameters.AddWithValue("$canonicalId", entity.CanonicalId);
        command.Parameters.AddWithValue("$sourceNamespace", entity.SourceNamespace);
        command.Parameters.AddWithValue("$maximumEvidenceRecords", MaximumEvidenceRecords);
        return ReadEvidenceRecords(command);
    }

    private static ReadOnlyCollection<GraphEvidenceRecord> ReadRelationshipEvidence(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        GraphRelationshipKey relationship)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                ingestion.id,
                ingestion.source_kind,
                ingestion.source_id,
                ingestion.source_name,
                ingestion.cluster_uri,
                ingestion.database_name,
                ingestion.query_text,
                ingestion.completed_at_utc,
                MAX(observation.discovered_at_utc),
                occurrence.table_name,
                occurrence.row_ordinal,
                payload.schema_json,
                payload.row_json
            FROM graph_relationship_observations observation
            INNER JOIN graph_relationship_observation_evidence linked
                ON linked.observation_id = observation.id
            INNER JOIN graph_evidence_occurrences occurrence
                ON occurrence.generation_id = linked.generation_id
                AND occurrence.occurrence_id = linked.occurrence_id
            INNER JOIN graph_evidence_payloads payload
                ON payload.content_hash = occurrence.content_hash
            INNER JOIN graph_ingestions ingestion
                ON ingestion.id = occurrence.ingestion_id
            WHERE observation.generation_id = $generationId
                AND observation.source_kind = $sourceKind
                AND observation.source_type_name = $sourceTypeName
                AND observation.source_canonical_id = $sourceCanonicalId
                AND observation.source_namespace = $sourceNamespace
                AND observation.target_kind = $targetKind
                AND observation.target_type_name = $targetTypeName
                AND observation.target_canonical_id = $targetCanonicalId
                AND observation.target_namespace = $targetNamespace
                AND observation.relationship_type = $relationshipType
                AND observation.discriminator = $discriminator
            GROUP BY occurrence.generation_id, occurrence.occurrence_id
            ORDER BY MAX(observation.discovered_at_utc) DESC, occurrence.occurrence_id DESC
            LIMIT $maximumEvidenceRecords;
            """;
        AddRelationshipParameters(command, snapshot.GenerationId, relationship);
        command.Parameters.AddWithValue("$maximumEvidenceRecords", MaximumEvidenceRecords);
        return ReadEvidenceRecords(command);
    }

    private static ReadOnlyCollection<GraphEvidenceRecord> ReadEvidenceRecords(SqliteCommand command)
    {
        List<GraphEvidenceRecord> evidence = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            Guid? sourceId = reader.IsDBNull(2)
                ? null
                : Guid.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            evidence.Add(new GraphEvidenceRecord(
                Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                (GraphIngestionSourceKind)reader.GetInt32(1),
                sourceId,
                reader.GetString(3),
                new Uri(reader.GetString(4), UriKind.Absolute),
                reader.GetString(5),
                reader.GetString(6),
                ParseTimestamp(reader.GetString(7)),
                ParseTimestamp(reader.GetString(8)),
                reader.GetString(9),
                reader.GetInt32(10),
                reader.GetString(11),
                reader.GetString(12)));
        }

        return evidence.AsReadOnly();
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
    }
}
