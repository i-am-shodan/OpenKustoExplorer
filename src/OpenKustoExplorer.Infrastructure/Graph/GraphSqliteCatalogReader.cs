using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Reads named graph catalog entries and validates immutable graph snapshots.
/// </summary>
internal static class GraphSqliteCatalogReader
{
    /// <summary>
    /// Reads every saved graph and the sole active graph.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The optional current transaction.</param>
    /// <returns>The complete graph catalog.</returns>
    internal static GraphCatalog ReadCatalog(
        SqliteConnection connection,
        SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                state.active_graph_id,
                catalog.id,
                catalog.name,
                catalog.description,
                catalog.active_generation_id,
                catalog.created_at_utc,
                catalog.last_updated_at_utc,
                catalog.last_activated_at_utc,
                (SELECT COUNT(*) FROM graph_entities entity
                    WHERE entity.generation_id = catalog.active_generation_id),
                (SELECT COUNT(*) FROM graph_relationships relationship
                    WHERE relationship.generation_id = catalog.active_generation_id)
            FROM graph_catalog catalog
            CROSS JOIN graph_state state
            WHERE state.singleton_id = 1
            ORDER BY
                catalog.last_activated_at_utc DESC,
                catalog.name COLLATE NOCASE,
                catalog.id;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        Guid activeGraphId = Guid.Empty;
        List<GraphCatalogEntry> graphs = [];

        while (reader.Read())
        {
            activeGraphId = Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            graphs.Add(ReadEntry(reader, 1));
        }

        if (graphs.Count == 0 || activeGraphId == Guid.Empty)
        {
            throw new InvalidDataException("The graph database has no readable named graph catalog.");
        }

        return new GraphCatalog(activeGraphId, graphs);
    }

    /// <summary>
    /// Gets one graph's current immutable snapshot.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="graphId">The graph identifier.</param>
    /// <param name="transaction">The optional current transaction.</param>
    /// <returns>The graph's current snapshot.</returns>
    internal static GraphSnapshot ReadSnapshot(
        SqliteConnection connection,
        Guid graphId,
        SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT active_generation_id FROM graph_catalog WHERE id = $graphId;";
        command.Parameters.AddWithValue("$graphId", FormatGuid(graphId));
        string generationText = command.ExecuteScalar() as string
            ?? throw new InvalidOperationException("The selected graph no longer exists.");
        return new GraphSnapshot(
            graphId,
            Guid.Parse(generationText, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Ensures a pinned mutation still targets the graph's current generation.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="transaction">The current write transaction.</param>
    /// <param name="target">The pinned mutation target.</param>
    internal static void ValidateWriteTarget(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphWriteTarget target)
    {
        ValidateSnapshot(connection, target.Snapshot, transaction);
    }

    /// <summary>
    /// Ensures an immutable snapshot remains the graph's current generation.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The snapshot to validate.</param>
    /// <param name="transaction">The optional current transaction.</param>
    internal static void ValidateSnapshot(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        SqliteTransaction? transaction = null)
    {
        GraphSnapshot current = ReadSnapshot(connection, snapshot.GraphId, transaction);

        if (current.GenerationId != snapshot.GenerationId)
        {
            throw new InvalidOperationException(
                "The graph changed after this operation started. Refresh the graph and try again.");
        }
    }

    /// <summary>
    /// Ensures a graph generation remains retained without requiring it to be current.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The retained graph snapshot.</param>
    internal static void ValidateRetainedSnapshot(
        SqliteConnection connection,
        GraphSnapshot snapshot)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM graph_generations generation
            WHERE generation.graph_id = $graphId
                AND generation.id = $generationId;
            """;
        command.Parameters.AddWithValue("$graphId", FormatGuid(snapshot.GraphId));
        command.Parameters.AddWithValue("$generationId", FormatGuid(snapshot.GenerationId));
        long matchingSnapshots = (long)(command.ExecuteScalar() ?? 0L);

        if (matchingSnapshots != 1)
        {
            throw new InvalidOperationException("The selected graph generation is no longer retained.");
        }
    }

    private static string FormatGuid(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);

    private static GraphCatalogEntry ReadEntry(SqliteDataReader reader, int offset)
    {
        return new GraphCatalogEntry(
            Guid.Parse(reader.GetString(offset), CultureInfo.InvariantCulture),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            Guid.Parse(reader.GetString(offset + 3), CultureInfo.InvariantCulture),
            ParseTimestamp(reader.GetString(offset + 4)),
            ParseTimestamp(reader.GetString(offset + 5)),
            ParseTimestamp(reader.GetString(offset + 6)),
            reader.GetInt64(offset + 7),
            reader.GetInt64(offset + 8));
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
    }
}
