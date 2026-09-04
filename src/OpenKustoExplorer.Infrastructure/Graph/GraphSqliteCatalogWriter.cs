using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Mutates named graph catalog metadata inside caller-owned transactions.
/// </summary>
internal static class GraphSqliteCatalogWriter
{
    /// <summary>
    /// Creates and activates an empty named graph.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="name">The normalized display name.</param>
    /// <param name="normalizedName">The case-insensitive uniqueness key.</param>
    /// <param name="description">The normalized description.</param>
    /// <param name="createdAtUtc">The graph creation time.</param>
    /// <returns>The new empty graph snapshot.</returns>
    internal static GraphSnapshot CreateGraph(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        string normalizedName,
        string description,
        DateTimeOffset createdAtUtc)
    {
        Guid graphId = Guid.NewGuid();
        Guid generationId = Guid.NewGuid();
        string timestamp = FormatTimestamp(createdAtUtc);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO graph_catalog (
                id,
                name,
                normalized_name,
                description,
                created_at_utc,
                last_updated_at_utc,
                last_activated_at_utc,
                active_generation_id
            ) VALUES (
                $graphId,
                $name,
                $normalizedName,
                $description,
                $timestamp,
                $timestamp,
                $timestamp,
                $generationId
            );
            INSERT INTO graph_generations (id, graph_id, created_at_utc, last_updated_at_utc)
            VALUES ($generationId, $graphId, $timestamp, $timestamp);
            UPDATE graph_state
            SET active_graph_id = $graphId
            WHERE singleton_id = 1;
            """;
        command.Parameters.AddWithValue("$graphId", FormatGuid(graphId));
        command.Parameters.AddWithValue("$generationId", FormatGuid(generationId));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$normalizedName", normalizedName);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$timestamp", timestamp);

        if (command.ExecuteNonQuery() != 3)
        {
            throw new InvalidDataException("The named graph could not be created and activated.");
        }

        return new GraphSnapshot(graphId, generationId);
    }

    /// <summary>
    /// Updates graph catalog metadata.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="graphId">The graph to update.</param>
    /// <param name="name">The normalized display name.</param>
    /// <param name="normalizedName">The case-insensitive uniqueness key.</param>
    /// <param name="description">The normalized graph description.</param>
    /// <param name="updatedAtUtc">When the metadata changed.</param>
    internal static void UpdateGraph(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid graphId,
        string name,
        string normalizedName,
        string description,
        DateTimeOffset updatedAtUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE graph_catalog
            SET name = $name,
                normalized_name = $normalizedName,
                description = $description,
                last_updated_at_utc = $updatedAt
            WHERE id = $graphId;
            """;
        command.Parameters.AddWithValue("$graphId", FormatGuid(graphId));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$normalizedName", normalizedName);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAtUtc));

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The selected graph no longer exists.");
        }
    }

    /// <summary>
    /// Activates one existing graph without changing its generation.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="graphId">The graph to activate.</param>
    /// <param name="activatedAtUtc">When the graph was activated.</param>
    internal static void ActivateGraph(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid graphId,
        DateTimeOffset activatedAtUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE graph_catalog
            SET last_activated_at_utc = $activatedAt
            WHERE id = $graphId;
            UPDATE graph_state
            SET active_graph_id = $graphId
            WHERE singleton_id = 1
                AND EXISTS (SELECT 1 FROM graph_catalog WHERE id = $graphId);
            """;
        command.Parameters.AddWithValue("$graphId", FormatGuid(graphId));
        command.Parameters.AddWithValue("$activatedAt", FormatTimestamp(activatedAtUtc));

        if (command.ExecuteNonQuery() != 2)
        {
            throw new InvalidOperationException("The selected graph no longer exists.");
        }
    }

    /// <summary>
    /// Updates the catalog timestamp after graph data changes.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="graphId">The graph whose data changed.</param>
    /// <param name="updatedAtUtc">When the data changed.</param>
    internal static void UpdateGraphTimestamp(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid graphId,
        DateTimeOffset updatedAtUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE graph_catalog
            SET last_updated_at_utc = CASE
                WHEN last_updated_at_utc < $updatedAt THEN $updatedAt
                ELSE last_updated_at_utc
            END
            WHERE id = $graphId;
            """;
        command.Parameters.AddWithValue("$graphId", FormatGuid(graphId));
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAtUtc));

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The selected graph no longer exists.");
        }
    }

    /// <summary>
    /// Deletes one graph and all generation-owned rows after selecting a fallback when necessary.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="graphId">The graph to delete.</param>
    internal static void DeleteGraph(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid graphId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM graph_catalog;
            """;
        long graphCount = (long)(command.ExecuteScalar() ?? 0L);

        if (graphCount <= 1)
        {
            throw new InvalidOperationException("The final saved graph cannot be deleted.");
        }

        command.CommandText = """
            SELECT COUNT(*) FROM graph_catalog WHERE id = $graphId;
            """;
        command.Parameters.AddWithValue("$graphId", FormatGuid(graphId));

        if ((long)(command.ExecuteScalar() ?? 0L) != 1)
        {
            throw new InvalidOperationException("The selected graph no longer exists.");
        }

        command.CommandText = """
            UPDATE graph_state
            SET active_graph_id = (
                SELECT id
                FROM graph_catalog
                WHERE id <> $graphId
                ORDER BY last_activated_at_utc DESC, name COLLATE NOCASE, id
                LIMIT 1
            )
            WHERE singleton_id = 1 AND active_graph_id = $graphId;

            DELETE FROM graph_relationship_observation_evidence
            WHERE generation_id IN (SELECT id FROM graph_generations WHERE graph_id = $graphId);
            DELETE FROM graph_entity_observation_evidence
            WHERE generation_id IN (SELECT id FROM graph_generations WHERE graph_id = $graphId);
            DELETE FROM graph_relationship_observations
            WHERE generation_id IN (SELECT id FROM graph_generations WHERE graph_id = $graphId);
            DELETE FROM graph_entity_observations
            WHERE generation_id IN (SELECT id FROM graph_generations WHERE graph_id = $graphId);
            DELETE FROM graph_relationships
            WHERE generation_id IN (SELECT id FROM graph_generations WHERE graph_id = $graphId);
            DELETE FROM graph_entities
            WHERE generation_id IN (SELECT id FROM graph_generations WHERE graph_id = $graphId);
            DELETE FROM graph_entity_search
            WHERE generation_id IN (SELECT id FROM graph_generations WHERE graph_id = $graphId);
            DELETE FROM graph_evidence_occurrences
            WHERE generation_id IN (SELECT id FROM graph_generations WHERE graph_id = $graphId);
            DELETE FROM graph_ingestions
            WHERE generation_id IN (SELECT id FROM graph_generations WHERE graph_id = $graphId);
            DELETE FROM graph_generations WHERE graph_id = $graphId;
            DELETE FROM graph_catalog WHERE id = $graphId;
            DELETE FROM graph_evidence_payloads
            WHERE NOT EXISTS (
                SELECT 1
                FROM graph_evidence_occurrences occurrence
                WHERE occurrence.content_hash = graph_evidence_payloads.content_hash
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes every graph except one newly created replacement and removes orphaned evidence payloads.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="replacementGraphId">The sole graph retained after deletion.</param>
    internal static void DeleteAllGraphsExcept(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid replacementGraphId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM graph_relationship_observation_evidence
            WHERE generation_id IN (
                SELECT id FROM graph_generations WHERE graph_id <> $replacementGraphId
            );
            DELETE FROM graph_entity_observation_evidence
            WHERE generation_id IN (
                SELECT id FROM graph_generations WHERE graph_id <> $replacementGraphId
            );
            DELETE FROM graph_relationship_observations
            WHERE generation_id IN (
                SELECT id FROM graph_generations WHERE graph_id <> $replacementGraphId
            );
            DELETE FROM graph_entity_observations
            WHERE generation_id IN (
                SELECT id FROM graph_generations WHERE graph_id <> $replacementGraphId
            );
            DELETE FROM graph_relationships
            WHERE generation_id IN (
                SELECT id FROM graph_generations WHERE graph_id <> $replacementGraphId
            );
            DELETE FROM graph_entities
            WHERE generation_id IN (
                SELECT id FROM graph_generations WHERE graph_id <> $replacementGraphId
            );
            DELETE FROM graph_entity_search
            WHERE generation_id IN (
                SELECT id FROM graph_generations WHERE graph_id <> $replacementGraphId
            );
            DELETE FROM graph_evidence_occurrences
            WHERE generation_id IN (
                SELECT id FROM graph_generations WHERE graph_id <> $replacementGraphId
            );
            DELETE FROM graph_ingestions
            WHERE generation_id IN (
                SELECT id FROM graph_generations WHERE graph_id <> $replacementGraphId
            );
            DELETE FROM graph_entity_label_overrides WHERE graph_id <> $replacementGraphId;
            DELETE FROM graph_generations WHERE graph_id <> $replacementGraphId;
            DELETE FROM graph_catalog WHERE id <> $replacementGraphId;
            DELETE FROM graph_evidence_payloads
            WHERE NOT EXISTS (
                SELECT 1
                FROM graph_evidence_occurrences occurrence
                WHERE occurrence.content_hash = graph_evidence_payloads.content_hash
            );
            """;
        command.Parameters.AddWithValue("$replacementGraphId", FormatGuid(replacementGraphId));
        command.ExecuteNonQuery();
    }

    private static string FormatGuid(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
