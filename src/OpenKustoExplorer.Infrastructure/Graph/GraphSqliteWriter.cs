using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Writes normalized graph batches inside a caller-owned SQLite transaction.
/// </summary>
internal static class GraphSqliteWriter
{
    /// <summary>
    /// Activates a graph generation atomically within the current transaction.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="graphId">The graph that owns the generation.</param>
    /// <param name="generationId">The generation to activate.</param>
    internal static void ActivateGeneration(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid graphId,
        Guid generationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE graph_catalog
            SET active_generation_id = $generationId,
                last_updated_at_utc = (
                    SELECT last_updated_at_utc
                    FROM graph_generations
                    WHERE id = $generationId AND graph_id = $graphId
                )
            WHERE id = $graphId
                AND EXISTS (
                    SELECT 1
                    FROM graph_generations
                    WHERE id = $generationId AND graph_id = $graphId
                );
            """;
        command.Parameters.AddWithValue("$graphId", FormatGuid(graphId));
        command.Parameters.AddWithValue("$generationId", FormatGuid(generationId));

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidDataException("The named graph generation could not be activated.");
        }
    }

    /// <summary>
    /// Creates a new empty graph generation.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="graphId">The graph that will own the generation.</param>
    /// <param name="createdAtUtc">The generation creation time.</param>
    /// <returns>The new generation identifier.</returns>
    internal static Guid CreateGeneration(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid graphId,
        DateTimeOffset createdAtUtc)
    {
        Guid generationId = Guid.NewGuid();
        string timestamp = FormatTimestamp(createdAtUtc);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO graph_generations (id, graph_id, created_at_utc, last_updated_at_utc)
            VALUES ($id, $graphId, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", FormatGuid(generationId));
        command.Parameters.AddWithValue("$graphId", FormatGuid(graphId));
        command.Parameters.AddWithValue("$createdAt", timestamp);
        command.Parameters.AddWithValue("$updatedAt", timestamp);
        command.ExecuteNonQuery();
        return generationId;
    }

    /// <summary>
    /// Inserts one raw evidence payload and its unique discovery occurrence.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="generationId">The target graph generation.</param>
    /// <param name="ingestionId">The owning ingestion.</param>
    /// <param name="evidence">The raw evidence occurrence.</param>
    internal static void InsertEvidence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        Guid ingestionId,
        GraphEvidence evidence)
    {
        using (SqliteCommand payloadCommand = connection.CreateCommand())
        {
            payloadCommand.Transaction = transaction;
            payloadCommand.CommandText = """
                INSERT OR IGNORE INTO graph_evidence_payloads (content_hash, schema_json, row_json)
                VALUES ($contentHash, $schemaJson, $rowJson);
                """;
            payloadCommand.Parameters.AddWithValue("$contentHash", evidence.ContentHash);
            payloadCommand.Parameters.AddWithValue("$schemaJson", evidence.SchemaJson);
            payloadCommand.Parameters.AddWithValue("$rowJson", evidence.RowJson);
            int payloadsInserted = payloadCommand.ExecuteNonQuery();

            if (payloadsInserted == 0)
            {
                ValidateExistingEvidencePayload(connection, transaction, evidence);
            }
        }

        using SqliteCommand occurrenceCommand = connection.CreateCommand();
        occurrenceCommand.Transaction = transaction;
        occurrenceCommand.CommandText = """
            INSERT INTO graph_evidence_occurrences (
                generation_id,
                occurrence_id,
                ingestion_id,
                content_hash,
                table_name,
                row_ordinal
            ) VALUES (
                $generationId,
                $occurrenceId,
                $ingestionId,
                $contentHash,
                $tableName,
                $rowOrdinal
            );
            """;
        occurrenceCommand.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
        occurrenceCommand.Parameters.AddWithValue("$occurrenceId", evidence.OccurrenceId);
        occurrenceCommand.Parameters.AddWithValue("$ingestionId", FormatGuid(ingestionId));
        occurrenceCommand.Parameters.AddWithValue("$contentHash", evidence.ContentHash);
        occurrenceCommand.Parameters.AddWithValue("$tableName", evidence.TableName);
        occurrenceCommand.Parameters.AddWithValue("$rowOrdinal", evidence.RowOrdinal);
        occurrenceCommand.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts an entity observation and all of its evidence links.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="generationId">The target graph generation.</param>
    /// <param name="observation">The entity observation.</param>
    internal static void InsertEntityObservation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        GraphEntityObservation observation)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO graph_entity_observations (
                    id,
                    generation_id,
                    kind,
                    type_name,
                    canonical_id,
                    source_namespace,
                    display_label,
                    source_labels_json,
                    properties_json,
                    valid_from_utc,
                    valid_to_utc,
                    discovered_at_utc,
                    superseded_at_utc
                ) VALUES (
                    $id,
                    $generationId,
                    $kind,
                    $typeName,
                    $canonicalId,
                    $sourceNamespace,
                    $displayLabel,
                    $sourceLabelsJson,
                    $propertiesJson,
                    $validFrom,
                    $validTo,
                    $discoveredAt,
                    $supersededAt
                );
                """;
            command.Parameters.AddWithValue("$id", FormatGuid(observation.Id));
            command.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
            AddEntityKeyParameters(command, observation.Entity, string.Empty);
            command.Parameters.AddWithValue("$displayLabel", observation.DisplayLabel);
            command.Parameters.AddWithValue(
                "$sourceLabelsJson",
                GraphSqliteJson.WriteStrings(observation.SourceLabels));
            command.Parameters.AddWithValue(
                "$propertiesJson",
                GraphSqliteJson.WriteProperties(observation.Properties));
            AddTemporalParameters(command, observation.TemporalInterval);
            command.ExecuteNonQuery();
        }

        foreach (string evidenceId in observation.EvidenceIds)
        {
            using SqliteCommand evidenceCommand = connection.CreateCommand();
            evidenceCommand.Transaction = transaction;
            evidenceCommand.CommandText = """
                INSERT INTO graph_entity_observation_evidence (
                    observation_id,
                    generation_id,
                    occurrence_id
                ) VALUES ($observationId, $generationId, $occurrenceId);
                """;
            evidenceCommand.Parameters.AddWithValue("$observationId", FormatGuid(observation.Id));
            evidenceCommand.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
            evidenceCommand.Parameters.AddWithValue("$occurrenceId", evidenceId);
            evidenceCommand.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Inserts query provenance for one committed graph ingestion.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="generationId">The target graph generation.</param>
    /// <param name="ingestion">The complete query provenance.</param>
    internal static void InsertIngestion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        GraphIngestion ingestion)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO graph_ingestions (
                id,
                generation_id,
                source_kind,
                source_id,
                source_name,
                cluster_uri,
                database_name,
                query_text,
                started_at_utc,
                completed_at_utc
            ) VALUES (
                $id,
                $generationId,
                $sourceKind,
                $sourceId,
                $sourceName,
                $clusterUri,
                $databaseName,
                $queryText,
                $startedAt,
                $completedAt
            );
            """;
        command.Parameters.AddWithValue("$id", FormatGuid(ingestion.Id));
        command.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
        command.Parameters.AddWithValue("$sourceKind", (int)ingestion.SourceKind);
        command.Parameters.AddWithValue(
            "$sourceId",
            ingestion.SourceId is null ? DBNull.Value : FormatGuid(ingestion.SourceId.Value));
        command.Parameters.AddWithValue("$sourceName", ingestion.SourceName);
        command.Parameters.AddWithValue("$clusterUri", ingestion.ClusterUri.AbsoluteUri);
        command.Parameters.AddWithValue("$databaseName", ingestion.DatabaseName);
        command.Parameters.AddWithValue("$queryText", ingestion.QueryText);
        command.Parameters.AddWithValue("$startedAt", FormatTimestamp(ingestion.StartedAtUtc));
        command.Parameters.AddWithValue("$completedAt", FormatTimestamp(ingestion.CompletedAtUtc));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts a relationship observation and all of its evidence links.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="generationId">The target graph generation.</param>
    /// <param name="observation">The relationship observation.</param>
    internal static void InsertRelationshipObservation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        GraphRelationshipObservation observation)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO graph_relationship_observations (
                    id,
                    generation_id,
                    source_kind,
                    source_type_name,
                    source_canonical_id,
                    source_namespace,
                    target_kind,
                    target_type_name,
                    target_canonical_id,
                    target_namespace,
                    relationship_type,
                    discriminator,
                    source_labels_json,
                    properties_json,
                    valid_from_utc,
                    valid_to_utc,
                    discovered_at_utc,
                    superseded_at_utc
                ) VALUES (
                    $id,
                    $generationId,
                    $sourceKind,
                    $sourceTypeName,
                    $sourceCanonicalId,
                    $sourceNamespace,
                    $targetKind,
                    $targetTypeName,
                    $targetCanonicalId,
                    $targetNamespace,
                    $relationshipType,
                    $discriminator,
                    $sourceLabelsJson,
                    $propertiesJson,
                    $validFrom,
                    $validTo,
                    $discoveredAt,
                    $supersededAt
                );
                """;
            command.Parameters.AddWithValue("$id", FormatGuid(observation.Id));
            command.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
            AddRelationshipKeyParameters(command, observation.Relationship);
            command.Parameters.AddWithValue(
                "$sourceLabelsJson",
                GraphSqliteJson.WriteStrings(observation.SourceLabels));
            command.Parameters.AddWithValue(
                "$propertiesJson",
                GraphSqliteJson.WriteProperties(observation.Properties));
            AddTemporalParameters(command, observation.TemporalInterval);
            command.ExecuteNonQuery();
        }

        foreach (string evidenceId in observation.EvidenceIds)
        {
            using SqliteCommand evidenceCommand = connection.CreateCommand();
            evidenceCommand.Transaction = transaction;
            evidenceCommand.CommandText = """
                INSERT INTO graph_relationship_observation_evidence (
                    observation_id,
                    generation_id,
                    occurrence_id
                ) VALUES ($observationId, $generationId, $occurrenceId);
                """;
            evidenceCommand.Parameters.AddWithValue("$observationId", FormatGuid(observation.Id));
            evidenceCommand.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
            evidenceCommand.Parameters.AddWithValue("$occurrenceId", evidenceId);
            evidenceCommand.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Ensures an edge endpoint exists without replacing summary data from an explicit entity observation.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="generationId">The target graph generation.</param>
    /// <param name="entity">The endpoint identity.</param>
    /// <param name="discoveredAtUtc">When the endpoint relationship was discovered.</param>
    /// <returns><see langword="true"/> when a stub entity was newly added.</returns>
    internal static bool EnsureEntity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        GraphEntityKey entity,
        DateTimeOffset discoveredAtUtc)
    {
        string effectiveDisplayLabel = GetEffectiveDisplayLabel(
            connection,
            transaction,
            generationId,
            entity,
            entity.CanonicalId);
        bool wasAdded = InsertEntityIfMissing(
            connection,
            transaction,
            generationId,
            entity,
            effectiveDisplayLabel,
            discoveredAtUtc);

        if (wasAdded)
        {
            UpdateEntitySearchIndex(connection, transaction, generationId, entity);
        }

        return wasAdded;
    }

    /// <summary>
    /// Updates the last committed change time of a graph generation.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="generationId">The graph generation.</param>
    /// <param name="updatedAtUtc">The latest committed query completion time.</param>
    internal static void UpdateGeneration(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        DateTimeOffset updatedAtUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE graph_generations
            SET last_updated_at_utc = CASE
                WHEN last_updated_at_utc < $updatedAt THEN $updatedAt
                ELSE last_updated_at_utc
            END
            WHERE id = $generationId;
            """;
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAtUtc));
        command.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts or refreshes the current summary for one entity identity.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="generationId">The target graph generation.</param>
    /// <param name="entity">The entity identity.</param>
    /// <param name="displayLabel">The latest display label candidate.</param>
    /// <param name="discoveredAtUtc">When this observation was discovered.</param>
    /// <returns><see langword="true"/> when the entity identity was newly added.</returns>
    internal static bool UpsertEntity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        GraphEntityKey entity,
        string displayLabel,
        DateTimeOffset discoveredAtUtc)
    {
        string effectiveDisplayLabel = GetEffectiveDisplayLabel(
            connection,
            transaction,
            generationId,
            entity,
            displayLabel);
        string discoveredAt = FormatTimestamp(discoveredAtUtc);
        bool wasAdded = InsertEntityIfMissing(
            connection,
            transaction,
            generationId,
            entity,
            effectiveDisplayLabel,
            discoveredAtUtc);

        using SqliteCommand updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE graph_entities
            SET
                display_label = CASE
                    WHEN last_updated_at_utc <= $discoveredAt THEN $displayLabel
                    ELSE display_label
                END,
                last_updated_at_utc = CASE
                    WHEN last_updated_at_utc < $discoveredAt THEN $discoveredAt
                    ELSE last_updated_at_utc
                END
            WHERE generation_id = $generationId
                AND kind = $kind
                AND type_name = $typeName
                AND canonical_id = $canonicalId
                AND source_namespace = $sourceNamespace;
            """;
        updateCommand.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
        AddEntityKeyParameters(updateCommand, entity, string.Empty);
        updateCommand.Parameters.AddWithValue("$displayLabel", effectiveDisplayLabel);
        updateCommand.Parameters.AddWithValue("$discoveredAt", discoveredAt);
        updateCommand.ExecuteNonQuery();
        UpdateEntitySearchIndex(connection, transaction, generationId, entity);
        return wasAdded;
    }

    /// <summary>
    /// Persists and applies a graph-scoped entity label override across retained generations.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="snapshot">The current graph snapshot.</param>
    /// <param name="entity">The entity identity.</param>
    /// <param name="displayLabel">The normalized visible label.</param>
    /// <param name="updatedAtUtc">When the override changed.</param>
    internal static void SetEntityDisplayLabel(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphSnapshot snapshot,
        GraphEntityKey entity,
        string displayLabel,
        DateTimeOffset updatedAtUtc)
    {
        using (SqliteCommand existenceCommand = connection.CreateCommand())
        {
            existenceCommand.Transaction = transaction;
            existenceCommand.CommandText = """
                SELECT COUNT(*)
                FROM graph_entities
                WHERE generation_id = $generationId
                    AND kind = $kind
                    AND type_name = $typeName
                    AND canonical_id = $canonicalId
                    AND source_namespace = $sourceNamespace;
                """;
            existenceCommand.Parameters.AddWithValue("$generationId", FormatGuid(snapshot.GenerationId));
            AddEntityKeyParameters(existenceCommand, entity, string.Empty);
            long entityCount = (long)(existenceCommand.ExecuteScalar() ?? 0L);

            if (entityCount != 1)
            {
                throw new InvalidOperationException("The graph node no longer exists. Refresh the graph and try again.");
            }
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO graph_entity_label_overrides (
                graph_id,
                kind,
                type_name,
                canonical_id,
                source_namespace,
                display_label,
                updated_at_utc
            ) VALUES (
                $graphId,
                $kind,
                $typeName,
                $canonicalId,
                $sourceNamespace,
                $displayLabel,
                $updatedAt
            )
            ON CONFLICT (graph_id, kind, type_name, canonical_id, source_namespace) DO UPDATE SET
                display_label = excluded.display_label,
                updated_at_utc = excluded.updated_at_utc;

            UPDATE graph_entities
            SET display_label = $displayLabel
            WHERE generation_id IN (
                    SELECT id
                    FROM graph_generations
                    WHERE graph_id = $graphId
                )
                AND kind = $kind
                AND type_name = $typeName
                AND canonical_id = $canonicalId
                AND source_namespace = $sourceNamespace;

            UPDATE graph_entity_search
            SET display_label = $displayLabel
            WHERE generation_id IN (
                    SELECT id
                    FROM graph_generations
                    WHERE graph_id = $graphId
                )
                AND kind = $kind
                AND type_name = $typeName
                AND canonical_id = $canonicalId
                AND source_namespace = $sourceNamespace;

            UPDATE graph_generations
            SET last_updated_at_utc = $updatedAt
            WHERE id = $generationId;

            UPDATE graph_catalog
            SET last_updated_at_utc = $updatedAt
            WHERE id = $graphId;
            """;
        command.Parameters.AddWithValue("$graphId", FormatGuid(snapshot.GraphId));
        command.Parameters.AddWithValue("$generationId", FormatGuid(snapshot.GenerationId));
        AddEntityKeyParameters(command, entity, string.Empty);
        command.Parameters.AddWithValue("$displayLabel", displayLabel);
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAtUtc));

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts or refreshes the current summary for one directed relationship identity.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="generationId">The target graph generation.</param>
    /// <param name="relationship">The directed relationship identity.</param>
    /// <param name="discoveredAtUtc">When this observation was discovered.</param>
    /// <returns><see langword="true"/> when the relationship identity was newly added.</returns>
    internal static bool UpsertRelationship(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        GraphRelationshipKey relationship,
        DateTimeOffset discoveredAtUtc)
    {
        string discoveredAt = FormatTimestamp(discoveredAtUtc);
        using SqliteCommand insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT OR IGNORE INTO graph_relationships (
                generation_id,
                source_kind,
                source_type_name,
                source_canonical_id,
                source_namespace,
                target_kind,
                target_type_name,
                target_canonical_id,
                target_namespace,
                relationship_type,
                discriminator,
                first_discovered_at_utc,
                last_updated_at_utc
            ) VALUES (
                $generationId,
                $sourceKind,
                $sourceTypeName,
                $sourceCanonicalId,
                $sourceNamespace,
                $targetKind,
                $targetTypeName,
                $targetCanonicalId,
                $targetNamespace,
                $relationshipType,
                $discriminator,
                $discoveredAt,
                $discoveredAt
            );
            """;
        insertCommand.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
        AddRelationshipKeyParameters(insertCommand, relationship);
        insertCommand.Parameters.AddWithValue("$discoveredAt", discoveredAt);
        bool wasAdded = insertCommand.ExecuteNonQuery() == 1;

        using SqliteCommand updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE graph_relationships
            SET last_updated_at_utc = CASE
                WHEN last_updated_at_utc < $discoveredAt THEN $discoveredAt
                ELSE last_updated_at_utc
            END
            WHERE generation_id = $generationId
                AND source_kind = $sourceKind
                AND source_type_name = $sourceTypeName
                AND source_canonical_id = $sourceCanonicalId
                AND source_namespace = $sourceNamespace
                AND target_kind = $targetKind
                AND target_type_name = $targetTypeName
                AND target_canonical_id = $targetCanonicalId
                AND target_namespace = $targetNamespace
                AND relationship_type = $relationshipType
                AND discriminator = $discriminator;
            """;
        updateCommand.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
        AddRelationshipKeyParameters(updateCommand, relationship);
        updateCommand.Parameters.AddWithValue("$discoveredAt", discoveredAt);
        updateCommand.ExecuteNonQuery();
        return wasAdded;
    }

    private static void AddEntityKeyParameters(
        SqliteCommand command,
        GraphEntityKey entity,
        string prefix)
    {
        string kindParameter = prefix.Length == 0 ? "$kind" : $"${prefix}Kind";
        string typeParameter = prefix.Length == 0 ? "$typeName" : $"${prefix}TypeName";
        string idParameter = prefix.Length == 0 ? "$canonicalId" : $"${prefix}CanonicalId";
        string namespaceParameter = prefix.Length == 0 ? "$sourceNamespace" : $"${prefix}Namespace";
        command.Parameters.AddWithValue(kindParameter, (int)entity.Kind);
        command.Parameters.AddWithValue(typeParameter, entity.TypeName);
        command.Parameters.AddWithValue(idParameter, entity.CanonicalId);
        command.Parameters.AddWithValue(namespaceParameter, entity.SourceNamespace);
    }

    private static void AddRelationshipKeyParameters(
        SqliteCommand command,
        GraphRelationshipKey relationship)
    {
        AddEntityKeyParameters(command, relationship.Source, "source");
        AddEntityKeyParameters(command, relationship.Target, "target");
        command.Parameters.AddWithValue("$relationshipType", relationship.TypeName);
        command.Parameters.AddWithValue("$discriminator", relationship.Discriminator);
    }

    private static void AddTemporalParameters(
        SqliteCommand command,
        GraphTemporalInterval interval)
    {
        command.Parameters.AddWithValue(
            "$validFrom",
            interval.ValidFromUtc is null ? DBNull.Value : FormatTimestamp(interval.ValidFromUtc.Value));
        command.Parameters.AddWithValue(
            "$validTo",
            interval.ValidToUtc is null ? DBNull.Value : FormatTimestamp(interval.ValidToUtc.Value));
        command.Parameters.AddWithValue("$discoveredAt", FormatTimestamp(interval.DiscoveredAtUtc));
        command.Parameters.AddWithValue(
            "$supersededAt",
            interval.SupersededAtUtc is null ? DBNull.Value : FormatTimestamp(interval.SupersededAtUtc.Value));
    }

    private static string FormatGuid(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string GetEffectiveDisplayLabel(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        GraphEntityKey entity,
        string fallbackDisplayLabel)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT label.display_label
            FROM graph_entity_label_overrides label
            INNER JOIN graph_generations generation
                ON generation.graph_id = label.graph_id
            WHERE generation.id = $generationId
                AND label.kind = $kind
                AND label.type_name = $typeName
                AND label.canonical_id = $canonicalId
                AND label.source_namespace = $sourceNamespace;
            """;
        command.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
        AddEntityKeyParameters(command, entity, string.Empty);
        return command.ExecuteScalar() as string ?? fallbackDisplayLabel;
    }

    private static bool InsertEntityIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        GraphEntityKey entity,
        string displayLabel,
        DateTimeOffset discoveredAtUtc)
    {
        string discoveredAt = FormatTimestamp(discoveredAtUtc);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO graph_entities (
                generation_id,
                kind,
                type_name,
                canonical_id,
                source_namespace,
                display_label,
                first_discovered_at_utc,
                last_updated_at_utc
            ) VALUES (
                $generationId,
                $kind,
                $typeName,
                $canonicalId,
                $sourceNamespace,
                $displayLabel,
                $discoveredAt,
                $discoveredAt
            );
            """;
        command.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
        AddEntityKeyParameters(command, entity, string.Empty);
        command.Parameters.AddWithValue("$displayLabel", displayLabel);
        command.Parameters.AddWithValue("$discoveredAt", discoveredAt);
        return command.ExecuteNonQuery() == 1;
    }

    private static void ValidateExistingEvidencePayload(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphEvidence evidence)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM graph_evidence_payloads
            WHERE content_hash = $contentHash
                AND schema_json = $schemaJson
                AND row_json = $rowJson;
            """;
        command.Parameters.AddWithValue("$contentHash", evidence.ContentHash);
        command.Parameters.AddWithValue("$schemaJson", evidence.SchemaJson);
        command.Parameters.AddWithValue("$rowJson", evidence.RowJson);
        long matchingPayloads = (long)(command.ExecuteScalar() ?? 0L);

        if (matchingPayloads != 1)
        {
            throw new InvalidDataException(
                $"Evidence content hash '{evidence.ContentHash}' maps to conflicting payloads.");
        }
    }

    private static void UpdateEntitySearchIndex(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        GraphEntityKey entity)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM graph_entity_search
            WHERE generation_id = $generationId
                AND kind = $kind
                AND type_name = $typeName
                AND canonical_id = $canonicalId
                AND source_namespace = $sourceNamespace;

            INSERT INTO graph_entity_search (
                generation_id,
                kind,
                type_name,
                canonical_id,
                source_namespace,
                display_label
            )
            SELECT
                generation_id,
                kind,
                type_name,
                canonical_id,
                source_namespace,
                display_label
            FROM graph_entities
            WHERE generation_id = $generationId
                AND kind = $kind
                AND type_name = $typeName
                AND canonical_id = $canonicalId
                AND source_namespace = $sourceNamespace;
            """;
        command.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
        AddEntityKeyParameters(command, entity, string.Empty);
        command.ExecuteNonQuery();
    }
}
