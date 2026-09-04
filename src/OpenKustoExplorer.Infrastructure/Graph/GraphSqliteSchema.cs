using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Creates and versions the normalized local investigation-graph schema.
/// </summary>
internal static class GraphSqliteSchema
{
    private const int CurrentVersion = 3;
    private const string CreateSchemaSql = """
        CREATE TABLE IF NOT EXISTS graph_catalog (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL UNIQUE,
            description TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            last_updated_at_utc TEXT NOT NULL,
            last_activated_at_utc TEXT NOT NULL,
            active_generation_id TEXT NOT NULL,
            FOREIGN KEY (active_generation_id) REFERENCES graph_generations(id)
                DEFERRABLE INITIALLY DEFERRED
        );

        CREATE TABLE IF NOT EXISTS graph_generations (
            id TEXT PRIMARY KEY,
            graph_id TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            last_updated_at_utc TEXT NOT NULL,
            FOREIGN KEY (graph_id) REFERENCES graph_catalog(id)
                DEFERRABLE INITIALLY DEFERRED
        );

        CREATE TABLE IF NOT EXISTS graph_state (
            singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
            active_graph_id TEXT NOT NULL,
            FOREIGN KEY (active_graph_id) REFERENCES graph_catalog(id)
        );

        CREATE TABLE IF NOT EXISTS graph_ingestions (
            id TEXT PRIMARY KEY,
            generation_id TEXT NOT NULL,
            source_kind INTEGER NOT NULL,
            source_id TEXT NULL,
            source_name TEXT NOT NULL,
            cluster_uri TEXT NOT NULL,
            database_name TEXT NOT NULL,
            query_text TEXT NOT NULL,
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NOT NULL,
            FOREIGN KEY (generation_id) REFERENCES graph_generations(id)
        );

        CREATE TABLE IF NOT EXISTS graph_evidence_payloads (
            content_hash TEXT PRIMARY KEY,
            schema_json TEXT NOT NULL,
            row_json TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS graph_evidence_occurrences (
            generation_id TEXT NOT NULL,
            occurrence_id TEXT NOT NULL,
            ingestion_id TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            table_name TEXT NOT NULL,
            row_ordinal INTEGER NOT NULL,
            PRIMARY KEY (generation_id, occurrence_id),
            FOREIGN KEY (generation_id) REFERENCES graph_generations(id),
            FOREIGN KEY (ingestion_id) REFERENCES graph_ingestions(id),
            FOREIGN KEY (content_hash) REFERENCES graph_evidence_payloads(content_hash)
        );

        CREATE TABLE IF NOT EXISTS graph_entities (
            generation_id TEXT NOT NULL,
            kind INTEGER NOT NULL,
            type_name TEXT NOT NULL,
            canonical_id TEXT NOT NULL,
            source_namespace TEXT NOT NULL,
            display_label TEXT NOT NULL,
            first_discovered_at_utc TEXT NOT NULL,
            last_updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (generation_id, kind, type_name, canonical_id, source_namespace),
            FOREIGN KEY (generation_id) REFERENCES graph_generations(id)
        );

        CREATE TABLE IF NOT EXISTS graph_entity_label_overrides (
            graph_id TEXT NOT NULL,
            kind INTEGER NOT NULL,
            type_name TEXT NOT NULL,
            canonical_id TEXT NOT NULL,
            source_namespace TEXT NOT NULL,
            display_label TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (graph_id, kind, type_name, canonical_id, source_namespace),
            FOREIGN KEY (graph_id) REFERENCES graph_catalog(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS graph_entity_observations (
            id TEXT PRIMARY KEY,
            generation_id TEXT NOT NULL,
            kind INTEGER NOT NULL,
            type_name TEXT NOT NULL,
            canonical_id TEXT NOT NULL,
            source_namespace TEXT NOT NULL,
            display_label TEXT NOT NULL,
            source_labels_json TEXT NOT NULL,
            properties_json TEXT NOT NULL,
            valid_from_utc TEXT NULL,
            valid_to_utc TEXT NULL,
            discovered_at_utc TEXT NOT NULL,
            superseded_at_utc TEXT NULL,
            FOREIGN KEY (generation_id, kind, type_name, canonical_id, source_namespace)
                REFERENCES graph_entities(generation_id, kind, type_name, canonical_id, source_namespace)
        );

        CREATE TABLE IF NOT EXISTS graph_entity_observation_evidence (
            observation_id TEXT NOT NULL,
            generation_id TEXT NOT NULL,
            occurrence_id TEXT NOT NULL,
            PRIMARY KEY (observation_id, occurrence_id),
            FOREIGN KEY (observation_id) REFERENCES graph_entity_observations(id),
            FOREIGN KEY (generation_id, occurrence_id)
                REFERENCES graph_evidence_occurrences(generation_id, occurrence_id)
        );

        CREATE TABLE IF NOT EXISTS graph_relationships (
            generation_id TEXT NOT NULL,
            source_kind INTEGER NOT NULL,
            source_type_name TEXT NOT NULL,
            source_canonical_id TEXT NOT NULL,
            source_namespace TEXT NOT NULL,
            target_kind INTEGER NOT NULL,
            target_type_name TEXT NOT NULL,
            target_canonical_id TEXT NOT NULL,
            target_namespace TEXT NOT NULL,
            relationship_type TEXT NOT NULL,
            discriminator TEXT NOT NULL,
            first_discovered_at_utc TEXT NOT NULL,
            last_updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (
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
                discriminator
            ),
            FOREIGN KEY (
                generation_id,
                source_kind,
                source_type_name,
                source_canonical_id,
                source_namespace
            ) REFERENCES graph_entities(generation_id, kind, type_name, canonical_id, source_namespace),
            FOREIGN KEY (
                generation_id,
                target_kind,
                target_type_name,
                target_canonical_id,
                target_namespace
            ) REFERENCES graph_entities(generation_id, kind, type_name, canonical_id, source_namespace)
        );

        CREATE TABLE IF NOT EXISTS graph_relationship_observations (
            id TEXT PRIMARY KEY,
            generation_id TEXT NOT NULL,
            source_kind INTEGER NOT NULL,
            source_type_name TEXT NOT NULL,
            source_canonical_id TEXT NOT NULL,
            source_namespace TEXT NOT NULL,
            target_kind INTEGER NOT NULL,
            target_type_name TEXT NOT NULL,
            target_canonical_id TEXT NOT NULL,
            target_namespace TEXT NOT NULL,
            relationship_type TEXT NOT NULL,
            discriminator TEXT NOT NULL,
            source_labels_json TEXT NOT NULL,
            properties_json TEXT NOT NULL,
            valid_from_utc TEXT NULL,
            valid_to_utc TEXT NULL,
            discovered_at_utc TEXT NOT NULL,
            superseded_at_utc TEXT NULL,
            FOREIGN KEY (
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
                discriminator
            ) REFERENCES graph_relationships(
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
                discriminator
            )
        );

        CREATE TABLE IF NOT EXISTS graph_relationship_observation_evidence (
            observation_id TEXT NOT NULL,
            generation_id TEXT NOT NULL,
            occurrence_id TEXT NOT NULL,
            PRIMARY KEY (observation_id, occurrence_id),
            FOREIGN KEY (observation_id) REFERENCES graph_relationship_observations(id),
            FOREIGN KEY (generation_id, occurrence_id)
                REFERENCES graph_evidence_occurrences(generation_id, occurrence_id)
        );

        CREATE INDEX IF NOT EXISTS ix_graph_generations_graph
            ON graph_generations(graph_id, created_at_utc DESC, id);
        CREATE INDEX IF NOT EXISTS ix_graph_ingestions_generation
            ON graph_ingestions(generation_id, completed_at_utc);
        CREATE INDEX IF NOT EXISTS ix_graph_evidence_generation
            ON graph_evidence_occurrences(generation_id, ingestion_id);
        CREATE INDEX IF NOT EXISTS ix_graph_entities_type
            ON graph_entities(generation_id, kind, type_name);
        CREATE INDEX IF NOT EXISTS ix_graph_entity_observations_time
            ON graph_entity_observations(generation_id, valid_from_utc, discovered_at_utc);
        CREATE INDEX IF NOT EXISTS ix_graph_relationships_source
            ON graph_relationships(
                generation_id,
                source_kind,
                source_type_name,
                source_canonical_id,
                source_namespace
            );
        CREATE INDEX IF NOT EXISTS ix_graph_relationships_target
            ON graph_relationships(
                generation_id,
                target_kind,
                target_type_name,
                target_canonical_id,
                target_namespace
            );
        CREATE INDEX IF NOT EXISTS ix_graph_relationship_observations_time
            ON graph_relationship_observations(generation_id, valid_from_utc, discovered_at_utc);

        CREATE VIRTUAL TABLE IF NOT EXISTS graph_entity_search USING fts5(
            generation_id UNINDEXED,
            kind UNINDEXED,
            type_name,
            canonical_id,
            source_namespace UNINDEXED,
            display_label
        );
        """;

    /// <summary>
    /// Creates or migrates the schema and ensures one initial named graph exists.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    internal static void EnsureCreated(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        int existingVersion = GetVersion(connection);

        if (existingVersion > CurrentVersion)
        {
            throw new InvalidDataException(
                $"Graph database schema version {existingVersion} is newer than supported version {CurrentVersion}.");
        }

        if (existingVersion == 1)
        {
            GraphSqliteMigration.MigrateVersionOneToTwo(connection);
            existingVersion = 2;
        }

        if (existingVersion == 2)
        {
            GraphSqliteMigration.MigrateVersionTwoToThree(connection);
            return;
        }

        if (existingVersion == 0)
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            using (SqliteCommand schemaCommand = connection.CreateCommand())
            {
                schemaCommand.Transaction = transaction;
                schemaCommand.CommandText = CreateSchemaSql;
                schemaCommand.ExecuteNonQuery();
            }

            EnsureInitialGraph(connection, transaction);
            using (SqliteCommand versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = "PRAGMA user_version = 3;";
                versionCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private static void EnsureInitialGraph(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand stateCommand = connection.CreateCommand();
        stateCommand.Transaction = transaction;
        stateCommand.CommandText = "SELECT COUNT(*) FROM graph_state WHERE singleton_id = 1;";
        long stateCount = (long)(stateCommand.ExecuteScalar() ?? 0L);

        if (stateCount == 0)
        {
            Guid graphId = Guid.NewGuid();
            Guid generationId = Guid.NewGuid();
            string timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            using SqliteCommand generationCommand = connection.CreateCommand();
            generationCommand.Transaction = transaction;
            generationCommand.CommandText = """
                INSERT INTO graph_catalog (
                    id,
                    name,
                    normalized_name,
                    description,
                    created_at_utc,
                    last_updated_at_utc,
                    last_activated_at_utc,
                    active_generation_id
                ) VALUES ($graphId, $name, $normalizedName, '', $created, $updated, $activated, $generationId);
                INSERT INTO graph_generations (id, graph_id, created_at_utc, last_updated_at_utc)
                VALUES ($generationId, $graphId, $created, $updated);
                INSERT INTO graph_state (singleton_id, active_graph_id)
                VALUES (1, $graphId);
                """;
            const string DefaultGraphName = "Default graph";
            generationCommand.Parameters.AddWithValue("$graphId", graphId.ToString("D"));
            generationCommand.Parameters.AddWithValue("$generationId", generationId.ToString("D"));
            generationCommand.Parameters.AddWithValue("$name", DefaultGraphName);
            generationCommand.Parameters.AddWithValue(
                "$normalizedName",
                GraphCatalogMetadata.NormalizeNameKey(DefaultGraphName));
            generationCommand.Parameters.AddWithValue("$created", timestamp);
            generationCommand.Parameters.AddWithValue("$updated", timestamp);
            generationCommand.Parameters.AddWithValue("$activated", timestamp);
            generationCommand.ExecuteNonQuery();
        }
    }

    private static int GetVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
