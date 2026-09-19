using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using BoundNode = OpenKustoExplorer.Graph.Query.GraphCypherQueryEngine.BoundNode;
using BoundRelationship = OpenKustoExplorer.Graph.Query.GraphCypherQueryEngine.BoundRelationship;
using MatchRow = OpenKustoExplorer.Graph.Query.GraphCypherQueryEngine.MatchRow;

namespace OpenKustoExplorer.Infrastructure.Graph.Query;

/// <summary>
/// Supplies normalized SQLite graph matches to the backend-neutral openCypher engine.
/// </summary>
internal static class GraphCypherExecutor
{
    /// <summary>
    /// Executes a graph query and returns diagnostics instead of exposing parser failures.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="request">The pinned query and result bounds.</param>
    /// <param name="cancellationToken">A token that cancels row materialization.</param>
    /// <returns>The projected rows, matched viewport, and diagnostics.</returns>
    internal static GraphQueryResult Execute(
        SqliteConnection connection,
        GraphQueryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(request);
        GraphSqliteCatalogReader.ValidateSnapshot(connection, request.Snapshot);
        return GraphCypherQueryEngine.Execute(
            request,
            (query, token) => ReadMatches(connection, request.Snapshot, query, token),
            cancellationToken);
    }

    /// <summary>
    /// Reads bounded node labels and relationship types for Copilot and query authoring.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The graph generation to describe.</param>
    /// <param name="cancellationToken">A token that cancels row materialization.</param>
    /// <returns>The bounded query schema.</returns>
    [SuppressMessage(
        "StyleCop.CSharp.ReadabilityRules",
        "SA1118:Parameter should not span multiple lines",
        Justification = "Keeping each static schema query inline makes its bound and projection auditable.")]
    internal static GraphQuerySchema ReadSchema(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        GraphSqliteCatalogReader.ValidateSnapshot(connection, snapshot);
        IReadOnlyList<GraphQuerySchemaEntry> nodeLabels = ReadSchemaEntries(
            connection,
            snapshot.GenerationId,
            """
            SELECT type_name, COUNT(*)
            FROM graph_entities
            WHERE generation_id = $generationId
            GROUP BY type_name
            ORDER BY COUNT(*) DESC, type_name
            LIMIT 100;
            """,
            cancellationToken);
        IReadOnlyList<GraphQuerySchemaEntry> relationshipTypes = ReadSchemaEntries(
            connection,
            snapshot.GenerationId,
            """
            SELECT relationship_type, COUNT(*)
            FROM graph_relationships
            WHERE generation_id = $generationId
            GROUP BY relationship_type
            ORDER BY COUNT(*) DESC, relationship_type
            LIMIT 100;
            """,
            cancellationToken);
        return new GraphQuerySchema(snapshot, nodeLabels, relationshipTypes);
    }

    private static IEnumerable<MatchRow> ReadMatches(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        ParsedQuery query,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = new SqlCompiler(connection, query).Compile(snapshot.GenerationId);
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ReadMatch(reader, query);
        }
    }

    private static MatchRow ReadMatch(SqliteDataReader reader, ParsedQuery query)
    {
        int offset = 0;
        BoundNode first = ReadNode(reader, ref offset);
        List<BoundNode> nodes = [first];
        BoundRelationship? relationship = null;

        if (query.Relationship is not null)
        {
            nodes.Add(ReadNode(reader, ref offset));
            relationship = ReadRelationship(reader, ref offset);
        }

        return new MatchRow(query, nodes, relationship);
    }

    private static BoundNode ReadNode(SqliteDataReader reader, ref int offset)
    {
        GraphEntityKey key = new(
            (GraphEntityKind)reader.GetInt32(offset),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            reader.GetString(offset + 3));
        GraphEntitySummary summary = new(
            key,
            reader.GetString(offset + 4),
            ParseTimestamp(reader.GetString(offset + 5)),
            ParseTimestamp(reader.GetString(offset + 6)),
            reader.GetInt64(offset + 7));
        string propertiesJson = reader.GetString(offset + 8);
        IReadOnlyDictionary<string, string> properties = GraphSqliteJson.ReadProperties(propertiesJson)
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        IReadOnlyList<string> labels = GraphSqliteJson.ReadStrings(reader.GetString(offset + 9));
        offset += 10;
        return new BoundNode(summary, properties, labels, propertiesJson);
    }

    private static BoundRelationship ReadRelationship(SqliteDataReader reader, ref int offset)
    {
        GraphRelationshipKey key = new(
            ReadEntityKey(reader, offset),
            ReadEntityKey(reader, offset + 4),
            reader.GetString(offset + 8),
            reader.GetString(offset + 9));
        string propertiesJson = reader.GetString(offset + 12);
        IReadOnlyDictionary<string, string> properties = GraphSqliteJson.ReadProperties(propertiesJson)
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        BoundRelationship relationship = new(
            key,
            ParseTimestamp(reader.GetString(offset + 10)),
            ParseTimestamp(reader.GetString(offset + 11)),
            properties,
            GraphSqliteJson.ReadStrings(reader.GetString(offset + 13)),
            propertiesJson);
        offset += 14;
        return relationship;
    }

    private static GraphEntityKey ReadEntityKey(SqliteDataReader reader, int offset)
    {
        return new GraphEntityKey(
            (GraphEntityKind)reader.GetInt32(offset),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            reader.GetString(offset + 3));
    }

    private static ReadOnlyCollection<GraphQuerySchemaEntry> ReadSchemaEntries(
        SqliteConnection connection,
        Guid generationId,
        string commandText,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$generationId", generationId.ToString("D", CultureInfo.InvariantCulture));
        using SqliteDataReader reader = command.ExecuteReader();
        List<GraphQuerySchemaEntry> entries = [];

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new GraphQuerySchemaEntry(reader.GetString(0), reader.GetInt64(1), []));
        }

        return entries.AsReadOnly();
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    private sealed class SqlCompiler
    {
        private readonly SqliteCommand command;
        private readonly ParsedQuery query;

        internal SqlCompiler(SqliteConnection connection, ParsedQuery query)
        {
            command = connection.CreateCommand();
            this.query = query;
        }

        internal SqliteCommand Compile(Guid generationId)
        {
            StringBuilder sql = new();
            sql.Append("SELECT ");
            AppendNodeColumns(sql, "n0", "no0");

            if (query.Relationship is not null)
            {
                sql.Append(", ");
                AppendNodeColumns(sql, "n1", "no1");
                sql.Append(", ");
                AppendRelationshipColumns(sql, "r0", "ro0");
            }

            sql.Append(" FROM graph_entities n0 ");
            AppendNodeObservationJoin(sql, "n0", "no0");

            if (query.Relationship is RelationshipPattern relationship)
            {
                AppendRelationshipJoins(sql, relationship);
                AppendNodeObservationJoin(sql, "n1", "no1");
                AppendRelationshipObservationJoin(sql, "r0", "ro0");
            }

            sql.Append(" WHERE n0.generation_id = $generationId");
            AppendStructuralOrder(sql, query);
            sql.Append(';');
            command.Parameters.AddWithValue(
                "$generationId",
                generationId.ToString("D", CultureInfo.InvariantCulture));
#pragma warning disable CA2100, S2077 // SQL structure is compiler-generated; all query values are parameters.
            command.CommandText = sql.ToString();
#pragma warning restore CA2100, S2077
            return command;
        }

        private static void AppendStructuralOrder(StringBuilder sql, ParsedQuery query)
        {
            sql.Append(" ORDER BY n0.kind, n0.type_name, n0.canonical_id, n0.source_namespace");

            if (query.Relationship is not null)
            {
                sql.Append(", n1.kind, n1.type_name, n1.canonical_id, n1.source_namespace")
                    .Append(", r0.relationship_type, r0.discriminator");
            }
        }

        [SuppressMessage(
            "StyleCop.CSharp.ReadabilityRules",
            "SA1118:Parameter should not span multiple lines",
            Justification = "The multiline SQL projection is clearer when passed directly to the builder.")]
        private static void AppendNodeColumns(StringBuilder sql, string entityAlias, string observationAlias)
        {
            sql.Append(CultureInfo.InvariantCulture, $"""
                {entityAlias}.kind,
                {entityAlias}.type_name,
                {entityAlias}.canonical_id,
                {entityAlias}.source_namespace,
                {entityAlias}.display_label,
                {entityAlias}.first_discovered_at_utc,
                {entityAlias}.last_updated_at_utc,
                (
                    SELECT COUNT(*)
                    FROM graph_relationships degree_relationship
                    WHERE degree_relationship.generation_id = {entityAlias}.generation_id
                        AND (
                            (
                                degree_relationship.source_kind = {entityAlias}.kind
                                AND degree_relationship.source_type_name = {entityAlias}.type_name
                                AND degree_relationship.source_canonical_id = {entityAlias}.canonical_id
                                AND degree_relationship.source_namespace = {entityAlias}.source_namespace
                            )
                            OR (
                                degree_relationship.target_kind = {entityAlias}.kind
                                AND degree_relationship.target_type_name = {entityAlias}.type_name
                                AND degree_relationship.target_canonical_id = {entityAlias}.canonical_id
                                AND degree_relationship.target_namespace = {entityAlias}.source_namespace
                            )
                        )
                ),
                COALESCE({observationAlias}.properties_json, json_object()),
                COALESCE({observationAlias}.source_labels_json, '[]')
                """);
        }

        [SuppressMessage(
            "StyleCop.CSharp.ReadabilityRules",
            "SA1118:Parameter should not span multiple lines",
            Justification = "The multiline SQL projection is clearer when passed directly to the builder.")]
        private static void AppendRelationshipColumns(
            StringBuilder sql,
            string relationshipAlias,
            string observationAlias)
        {
            sql.Append(CultureInfo.InvariantCulture, $"""
                {relationshipAlias}.source_kind,
                {relationshipAlias}.source_type_name,
                {relationshipAlias}.source_canonical_id,
                {relationshipAlias}.source_namespace,
                {relationshipAlias}.target_kind,
                {relationshipAlias}.target_type_name,
                {relationshipAlias}.target_canonical_id,
                {relationshipAlias}.target_namespace,
                {relationshipAlias}.relationship_type,
                {relationshipAlias}.discriminator,
                {relationshipAlias}.first_discovered_at_utc,
                {relationshipAlias}.last_updated_at_utc,
                COALESCE({observationAlias}.properties_json, json_object()),
                COALESCE({observationAlias}.source_labels_json, '[]')
                """);
        }

        [SuppressMessage(
            "StyleCop.CSharp.ReadabilityRules",
            "SA1118:Parameter should not span multiple lines",
            Justification = "The multiline SQL join is clearer when passed directly to the builder.")]
        private static void AppendNodeObservationJoin(
            StringBuilder sql,
            string entityAlias,
            string observationAlias)
        {
            sql.Append(CultureInfo.InvariantCulture, $"""
                 LEFT JOIN graph_entity_observations {observationAlias}
                    ON {observationAlias}.id = (
                        SELECT latest.id
                        FROM graph_entity_observations latest
                        WHERE latest.generation_id = {entityAlias}.generation_id
                            AND latest.kind = {entityAlias}.kind
                            AND latest.type_name = {entityAlias}.type_name
                            AND latest.canonical_id = {entityAlias}.canonical_id
                            AND latest.source_namespace = {entityAlias}.source_namespace
                        ORDER BY latest.discovered_at_utc DESC, latest.id DESC
                        LIMIT 1
                    )
                """);
        }

        [SuppressMessage(
            "StyleCop.CSharp.ReadabilityRules",
            "SA1118:Parameter should not span multiple lines",
            Justification = "The multiline SQL join is clearer when passed directly to the builder.")]
        private static void AppendRelationshipObservationJoin(
            StringBuilder sql,
            string relationshipAlias,
            string observationAlias)
        {
            sql.Append(CultureInfo.InvariantCulture, $"""
                 LEFT JOIN graph_relationship_observations {observationAlias}
                    ON {observationAlias}.id = (
                        SELECT latest.id
                        FROM graph_relationship_observations latest
                        WHERE latest.generation_id = {relationshipAlias}.generation_id
                            AND latest.source_kind = {relationshipAlias}.source_kind
                            AND latest.source_type_name = {relationshipAlias}.source_type_name
                            AND latest.source_canonical_id = {relationshipAlias}.source_canonical_id
                            AND latest.source_namespace = {relationshipAlias}.source_namespace
                            AND latest.target_kind = {relationshipAlias}.target_kind
                            AND latest.target_type_name = {relationshipAlias}.target_type_name
                            AND latest.target_canonical_id = {relationshipAlias}.target_canonical_id
                            AND latest.target_namespace = {relationshipAlias}.target_namespace
                            AND latest.relationship_type = {relationshipAlias}.relationship_type
                            AND latest.discriminator = {relationshipAlias}.discriminator
                        ORDER BY latest.discovered_at_utc DESC, latest.id DESC
                        LIMIT 1
                    )
                """);
        }

        private static string EntityKeyEquals(
            string entityAlias,
            string relationshipAlias,
            bool sourceEndpoint)
        {
            string endpoint = sourceEndpoint ? "source" : "target";
            string namespaceColumn = sourceEndpoint ? "source_namespace" : "target_namespace";
            return $"""
                {relationshipAlias}.{endpoint}_kind = {entityAlias}.kind
                AND {relationshipAlias}.{endpoint}_type_name = {entityAlias}.type_name
                AND {relationshipAlias}.{endpoint}_canonical_id = {entityAlias}.canonical_id
                AND {relationshipAlias}.{namespaceColumn} = {entityAlias}.source_namespace
                """;
        }

        private static void AppendRelationshipJoins(StringBuilder sql, RelationshipPattern relationship)
        {
            string currentSource = EntityKeyEquals("n0", "r0", true);
            string currentTarget = EntityKeyEquals("n0", "r0", false);
            string nextSource = EntityKeyEquals("n1", "r0", true);
            string nextTarget = EntityKeyEquals("n1", "r0", false);
            string relationshipJoin = relationship.Direction switch
            {
                RelationshipDirection.Outgoing => currentSource,
                RelationshipDirection.Incoming => currentTarget,
                _ => $"(({currentSource}) OR ({currentTarget}))",
            };
            string entityJoin = relationship.Direction switch
            {
                RelationshipDirection.Outgoing => nextTarget,
                RelationshipDirection.Incoming => nextSource,
                _ => $"(({currentSource}) AND ({nextTarget})) OR (({currentTarget}) AND ({nextSource}))",
            };
            sql.Append(" JOIN graph_relationships r0 ON r0.generation_id = n0.generation_id AND (")
                .Append(relationshipJoin)
                .Append(')');
            sql.Append(" JOIN graph_entities n1 ON n1.generation_id = r0.generation_id AND (")
                .Append(entityJoin)
                .Append(')');
        }
    }
}
