using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Applies transactional graph database migrations without rewriting retained evidence rows.
/// </summary>
internal static class GraphSqliteMigration
{
    private const string MigrateVersionOneSql = """
        CREATE TABLE graph_catalog (
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

        INSERT INTO graph_catalog (
            id,
            name,
            normalized_name,
            description,
            created_at_utc,
            last_updated_at_utc,
            last_activated_at_utc,
            active_generation_id
        )
        SELECT
            $graphId,
            $name,
            $normalizedName,
            '',
            MIN(generation.created_at_utc),
            MAX(generation.last_updated_at_utc),
            $activatedAt,
            state.active_generation_id
        FROM graph_state state
        CROSS JOIN graph_generations generation
        WHERE state.singleton_id = 1;

        CREATE TABLE graph_generations_v2 (
            id TEXT PRIMARY KEY,
            graph_id TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            last_updated_at_utc TEXT NOT NULL,
            FOREIGN KEY (graph_id) REFERENCES graph_catalog(id)
                DEFERRABLE INITIALLY DEFERRED
        );

        INSERT INTO graph_generations_v2 (id, graph_id, created_at_utc, last_updated_at_utc)
        SELECT id, $graphId, created_at_utc, last_updated_at_utc
        FROM graph_generations;

        DROP TABLE graph_generations;
        ALTER TABLE graph_generations_v2 RENAME TO graph_generations;

        ALTER TABLE graph_state RENAME TO graph_state_v1;
        CREATE TABLE graph_state (
            singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
            active_graph_id TEXT NOT NULL,
            FOREIGN KEY (active_graph_id) REFERENCES graph_catalog(id)
        );
        INSERT INTO graph_state (singleton_id, active_graph_id)
        VALUES (1, $graphId);
        DROP TABLE graph_state_v1;

        CREATE INDEX ix_graph_generations_graph
            ON graph_generations(graph_id, created_at_utc DESC, id);
        PRAGMA user_version = 2;
        """;

    private const string MigrateVersionTwoSql = """
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
        PRAGMA user_version = 3;
        """;

    /// <summary>
    /// Migrates every retained version-one generation into one default named graph.
    /// </summary>
    /// <param name="connection">The open graph database connection.</param>
    internal static void MigrateVersionOneToTwo(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        const string DefaultGraphName = "Default graph";
        Guid graphId = Guid.NewGuid();
        string activatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SetForeignKeys(connection, enabled: false);

        try
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            using (SqliteCommand migrationCommand = connection.CreateCommand())
            {
                migrationCommand.Transaction = transaction;
                migrationCommand.CommandText = MigrateVersionOneSql;
                migrationCommand.Parameters.AddWithValue("$graphId", graphId.ToString("D"));
                migrationCommand.Parameters.AddWithValue("$name", DefaultGraphName);
                migrationCommand.Parameters.AddWithValue(
                    "$normalizedName",
                    GraphCatalogMetadata.NormalizeNameKey(DefaultGraphName));
                migrationCommand.Parameters.AddWithValue("$activatedAt", activatedAt);
                migrationCommand.ExecuteNonQuery();
            }

            ThrowIfForeignKeysInvalid(connection, transaction);
            transaction.Commit();
        }
        finally
        {
            SetForeignKeys(connection, enabled: true);
        }
    }

    /// <summary>
    /// Adds durable graph-scoped entity label overrides to a version-two database.
    /// </summary>
    /// <param name="connection">The open graph database connection.</param>
    internal static void MigrateVersionTwoToThree(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand migrationCommand = connection.CreateCommand())
        {
            migrationCommand.Transaction = transaction;
            migrationCommand.CommandText = MigrateVersionTwoSql;
            migrationCommand.ExecuteNonQuery();
        }

        ThrowIfForeignKeysInvalid(connection, transaction);
        transaction.Commit();
    }

    private static void SetForeignKeys(SqliteConnection connection, bool enabled)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = enabled ? "PRAGMA foreign_keys = ON;" : "PRAGMA foreign_keys = OFF;";
        command.ExecuteNonQuery();
    }

    private static void ThrowIfForeignKeysInvalid(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check;";
        using SqliteDataReader reader = command.ExecuteReader();

        if (reader.Read())
        {
            throw new InvalidDataException(
                $"Graph migration produced an invalid foreign key in table '{reader.GetString(0)}'.");
        }
    }
}
