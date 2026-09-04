using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;

namespace OpenKustoExplorer.Infrastructure.Graph.Query;

#pragma warning disable CA1859 // Private parser collections expose read-only interfaces shared by syntax nodes.
#pragma warning disable S134, S3267, S3358, S3776 // Recursive parsing and bounded graph accumulation are intentionally branch-oriented.
#pragma warning disable S3871 // The private parse exception carries source spans and never crosses the service boundary.
#pragma warning disable SA1118, SA1201, SA1204 // Private nested compiler/parser types are grouped by responsibility.

/// <summary>
/// Parses and executes the bounded read-only openCypher subset over normalized SQLite graph rows.
/// </summary>
internal static class GraphCypherExecutor
{
    private const int MaximumScannedMatchCount = 10_000;

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
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            GraphSqliteCatalogReader.ValidateSnapshot(connection, request.Snapshot);
            ParsedQuery query = new Parser(request.QueryText).Parse();
            using SqliteCommand command = CreateMatchCommand(
                connection,
                request.Snapshot,
                query,
                MaximumScannedMatchCount + 1);
            using SqliteDataReader reader = command.ExecuteReader();
            List<MatchRow> matches = [];

            while (reader.Read() && matches.Count <= MaximumScannedMatchCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                matches.Add(ReadMatch(reader, query));
            }

            bool scanTruncated = matches.Count > MaximumScannedMatchCount;
            if (scanTruncated)
            {
                matches.RemoveAt(matches.Count - 1);
            }

            Projection projection = Project(query, matches, request.MaximumRowCount);
            GraphViewport viewport = CreateViewport(
                projection.ViewportMatches,
                request.MaximumEntityCount,
                request.MaximumRelationshipCount,
                scanTruncated);
            List<GraphQueryDiagnostic> diagnostics = [];

            if (scanTruncated)
            {
                diagnostics.Add(new GraphQueryDiagnostic(
                    GraphQueryDiagnosticSeverity.Warning,
                    0,
                    request.QueryText.Length,
                    $"Matching stopped after {MaximumScannedMatchCount:N0} paths. Add a label, relationship type, filter, or limit."));
            }

            stopwatch.Stop();
            return new GraphQueryResult(
                request.Snapshot,
                request.QueryText,
                projection.Columns,
                projection.Rows,
                viewport,
                diagnostics,
                stopwatch.Elapsed,
                scanTruncated || projection.AreRowsTruncated);
        }
        catch (GraphCypherParseException exception)
        {
            stopwatch.Stop();
            GraphQueryDiagnostic diagnostic = new(
                GraphQueryDiagnosticSeverity.Error,
                exception.Start,
                exception.Length,
                exception.Message);
            return new GraphQueryResult(
                request.Snapshot,
                request.QueryText,
                [],
                [],
                new GraphViewport(null, [], [], false),
                [diagnostic],
                stopwatch.Elapsed,
                false);
        }
    }

    /// <summary>
    /// Reads bounded node labels and relationship types for Copilot and query authoring.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <param name="snapshot">The graph generation to describe.</param>
    /// <param name="cancellationToken">A token that cancels row materialization.</param>
    /// <returns>The bounded query schema.</returns>
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

    private static SqliteCommand CreateMatchCommand(
        SqliteConnection connection,
        GraphSnapshot snapshot,
        ParsedQuery query,
        int matchLimit)
    {
        SqlCompiler compiler = new(connection, query);
        return compiler.Compile(snapshot.GenerationId, matchLimit);
    }

    private static GraphViewport CreateViewport(
        IReadOnlyList<MatchRow> matches,
        int maximumEntityCount,
        int maximumRelationshipCount,
        bool inheritedTruncation)
    {
        Dictionary<GraphEntityKey, GraphEntitySummary> entities = [];
        List<GraphRelationshipKey> relationships = [];
        bool isTruncated = inheritedTruncation;

        foreach (MatchRow match in matches)
        {
            foreach (BoundNode node in match.Nodes)
            {
                if (!entities.ContainsKey(node.Summary.Entity))
                {
                    if (entities.Count < maximumEntityCount)
                    {
                        entities.Add(node.Summary.Entity, node.Summary);
                    }
                    else
                    {
                        isTruncated = true;
                    }
                }
            }

            if (match.Relationship is BoundRelationship relationship
                && entities.ContainsKey(relationship.Key.Source)
                && entities.ContainsKey(relationship.Key.Target)
                && !relationships.Contains(relationship.Key))
            {
                if (relationships.Count < maximumRelationshipCount)
                {
                    relationships.Add(relationship.Key);
                }
                else
                {
                    isTruncated = true;
                }
            }
        }

        GraphEntityKey? center = entities.Count == 0 ? null : entities.Keys.First();
        return new GraphViewport(center, entities.Values, relationships, isTruncated);
    }

    private static Projection Project(ParsedQuery query, IReadOnlyList<MatchRow> matches, int maximumRowCount)
    {
        bool hasAggregate = query.ReturnItems.Any(item => IsAggregate(item.Expression));

        if (hasAggregate && query.ReturnItems.Any(item => !IsAggregate(item.Expression)))
        {
            throw new GraphCypherParseException(
                query.ReturnStart,
                6,
                "Mixing aggregate and non-aggregate return expressions is not supported yet.");
        }

        if (hasAggregate)
        {
            return ProjectAggregates(query, matches);
        }

        List<ProjectedMatch> projected = matches
            .Select(match => new ProjectedMatch(
                match,
                query.ReturnItems.Select(item => Evaluate(item.Expression, match)).ToArray(),
                query.OrderItems.Select(item => Evaluate(item.Expression, match)).ToArray()))
            .ToList();

        if (query.IsDistinct)
        {
            projected = projected
                .DistinctBy(item => CreateValueKey(item.Values), StringComparer.Ordinal)
                .ToList();
        }

        ApplyOrdering(projected, query.OrderItems);
        int skip = Math.Min(query.Skip, projected.Count);
        int availableCount = projected.Count - skip;
        int requestedCount = query.Limit ?? maximumRowCount;
        int take = Math.Min(Math.Min(requestedCount, maximumRowCount), availableCount);
        List<ProjectedMatch> selected = projected.Skip(skip).Take(take).ToList();
        GraphQueryColumn[] columns = CreateColumns(query, selected.Select(item => item.Values));
        GraphQueryRow[] rows = selected
            .Select(item => new GraphQueryRow(item.Values.Select(ConvertValue)))
            .ToArray();
        bool rowsTruncated = availableCount > take && (query.Limit is null || query.Limit > maximumRowCount);
        return new Projection(columns, rows, selected.Select(item => item.Match).ToArray(), rowsTruncated);
    }

    private static Projection ProjectAggregates(ParsedQuery query, IReadOnlyList<MatchRow> matches)
    {
        EvalValue[] values = query.ReturnItems
            .Select(item => EvaluateAggregate(item.Expression, matches))
            .ToArray();
        GraphQueryColumn[] columns = CreateColumns(query, [values]);
        GraphQueryRow row = new(values.Select(ConvertValue));
        return new Projection(columns, [row], matches, false);
    }

    private static void ApplyOrdering(List<ProjectedMatch> projected, IReadOnlyList<OrderItem> orderItems)
    {
        if (orderItems.Count > 0)
        {
            projected.Sort((left, right) => CompareProjected(left, right, orderItems));
        }
    }

    private static int CompareProjected(
        ProjectedMatch left,
        ProjectedMatch right,
        IReadOnlyList<OrderItem> orderItems)
    {
        int comparison = 0;

        for (int index = 0; index < orderItems.Count && comparison == 0; index++)
        {
            comparison = CompareValues(left.OrderValues[index], right.OrderValues[index]);

            if (orderItems[index].IsDescending)
            {
                comparison = -comparison;
            }
        }

        return comparison;
    }

    private static int CompareValues(EvalValue left, EvalValue right)
    {
        int comparison;

        if (left.Kind == EvalKind.Null || right.Kind == EvalKind.Null)
        {
            comparison = left.Kind == right.Kind ? 0 : left.Kind == EvalKind.Null ? -1 : 1;
        }
        else if (left.Number is double leftNumber && right.Number is double rightNumber)
        {
            comparison = leftNumber.CompareTo(rightNumber);
        }
        else
        {
            comparison = string.Compare(left.Text, right.Text, StringComparison.Ordinal);
        }

        return comparison;
    }

    private static GraphQueryColumn[] CreateColumns(
        ParsedQuery query,
        IEnumerable<IReadOnlyList<EvalValue>> projectedValues)
    {
        IReadOnlyList<EvalValue>[] rows = projectedValues.ToArray();
        GraphQueryColumn[] columns = new GraphQueryColumn[query.ReturnItems.Count];

        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            EvalValue? sample = rows
                .Select(row => row[columnIndex])
                .FirstOrDefault(value => value.Kind != EvalKind.Null);
            GraphQueryValueKind kind;

            if (sample is null)
            {
                kind = GraphQueryValueKind.Null;
            }
            else if (sample.Kind == EvalKind.Number && sample.IsWholeNumber)
            {
                kind = GraphQueryValueKind.WholeNumber;
            }
            else
            {
                kind = GetPublicKind(sample.Kind);
            }

            ReturnItem item = query.ReturnItems[columnIndex];
            columns[columnIndex] = new GraphQueryColumn(item.Alias ?? item.ExpressionText, kind);
        }

        return columns;
    }

    private static EvalValue EvaluateAggregate(Expression expression, IReadOnlyList<MatchRow> matches)
    {
        FunctionExpression function = expression as FunctionExpression
            ?? throw new InvalidOperationException("Expected an aggregate function.");
        long count;

        if (function.IsStarArgument)
        {
            count = matches.Count;
        }
        else
        {
            IEnumerable<EvalValue> values = matches.Select(match => Evaluate(function.Arguments[0], match));
            values = values.Where(value => value.Kind != EvalKind.Null);

            if (function.IsDistinct)
            {
                values = values.DistinctBy(value => CreateValueKey([value]), StringComparer.Ordinal);
            }

            count = values.LongCount();
        }

        return EvalValue.FromNumber(count);
    }

    private static EvalValue Evaluate(Expression expression, MatchRow match)
    {
        return expression switch
        {
            LiteralExpression literal => EvalValue.FromLiteral(literal),
            VariableExpression variable => match.GetVariable(variable.Name),
            PropertyExpression property => EvaluateProperty(property, match),
            FunctionExpression function => EvaluateFunction(function, match),
            _ => throw new InvalidOperationException("This expression cannot be projected."),
        };
    }

    private static EvalValue EvaluateFunction(FunctionExpression function, MatchRow match)
    {
        string functionName = function.Name.ToUpperInvariant();
        EvalValue argument = function.Arguments.Count == 0
            ? EvalValue.Null
            : Evaluate(function.Arguments[0], match);
        return functionName switch
        {
            "TYPE" when argument.Relationship is BoundRelationship relationship =>
                EvalValue.FromText(relationship.Key.TypeName),
            "LABELS" when argument.Node is BoundNode node =>
                EvalValue.FromText($"[{string.Join(", ", node.Labels)}]"),
            "ELEMENTID" when argument.Node is BoundNode node =>
                EvalValue.FromText(node.Summary.Entity.ToString()),
            "ELEMENTID" when argument.Relationship is BoundRelationship relationship =>
                EvalValue.FromText(relationship.Key.ToString()),
            "PROPERTIES" when argument.Node is BoundNode node => EvalValue.FromText(node.PropertiesJson),
            "PROPERTIES" when argument.Relationship is BoundRelationship relationship =>
                EvalValue.FromText(relationship.PropertiesJson),
            "TOLOWER" when argument.Kind != EvalKind.Null => EvalValue.FromText(argument.Text.ToLowerInvariant()),
            "TOUPPER" when argument.Kind != EvalKind.Null => EvalValue.FromText(argument.Text.ToUpperInvariant()),
            _ => EvalValue.Null,
        };
    }

    private static EvalValue EvaluateProperty(PropertyExpression property, MatchRow match)
    {
        EvalValue owner = match.GetVariable(property.VariableName);
        EvalValue value = EvalValue.Null;

        if (owner.Node is BoundNode node)
        {
            value = property.PropertyName switch
            {
                "canonicalId" => EvalValue.FromText(node.Summary.Entity.CanonicalId),
                "displayLabel" => EvalValue.FromText(node.Summary.DisplayLabel),
                "typeName" => EvalValue.FromText(node.Summary.Entity.TypeName),
                "kind" => EvalValue.FromText(node.Summary.Entity.Kind.ToString()),
                "sourceNamespace" => EvalValue.FromText(node.Summary.Entity.SourceNamespace),
                "firstDiscoveredAt" => EvalValue.FromText(node.Summary.FirstDiscoveredAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                "lastUpdatedAt" => EvalValue.FromText(node.Summary.LastUpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                "degree" => EvalValue.FromNumber(node.Summary.Degree),
                _ => node.Properties.TryGetValue(property.PropertyName, out string? propertyValue)
                    ? EvalValue.FromText(propertyValue)
                    : EvalValue.Null,
            };
        }
        else if (owner.Relationship is BoundRelationship relationship)
        {
            value = property.PropertyName switch
            {
                "typeName" => EvalValue.FromText(relationship.Key.TypeName),
                "discriminator" => EvalValue.FromText(relationship.Key.Discriminator),
                "firstDiscoveredAt" => EvalValue.FromText(relationship.FirstDiscoveredAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                "lastUpdatedAt" => EvalValue.FromText(relationship.LastUpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                _ => relationship.Properties.TryGetValue(property.PropertyName, out string? propertyValue)
                    ? EvalValue.FromText(propertyValue)
                    : EvalValue.Null,
            };
        }

        return value;
    }

    private static GraphQueryValue ConvertValue(EvalValue value)
    {
        return value.Kind switch
        {
            EvalKind.Null => GraphQueryValue.FromNull(),
            EvalKind.Number when value.IsWholeNumber => GraphQueryValue.FromInteger((long)value.Number!.Value),
            EvalKind.Number => GraphQueryValue.FromFloatingPoint(value.Number!.Value),
            EvalKind.Boolean => GraphQueryValue.FromBoolean(value.Boolean!.Value),
            EvalKind.Node => GraphQueryValue.FromEntity(value.Node!.Summary.Entity),
            EvalKind.Relationship => GraphQueryValue.FromRelationship(value.Relationship!.Key),
            _ => GraphQueryValue.FromString(value.Text),
        };
    }

    private static GraphQueryValueKind GetPublicKind(EvalKind kind)
    {
        return kind switch
        {
            EvalKind.Null => GraphQueryValueKind.Null,
            EvalKind.Number => GraphQueryValueKind.FloatingPoint,
            EvalKind.Boolean => GraphQueryValueKind.Boolean,
            EvalKind.Node => GraphQueryValueKind.Entity,
            EvalKind.Relationship => GraphQueryValueKind.Relationship,
            _ => GraphQueryValueKind.Text,
        };
    }

    private static bool IsAggregate(Expression expression)
    {
        return expression is FunctionExpression function
            && function.Name.Equals("count", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateValueKey(IReadOnlyList<EvalValue> values)
    {
        return string.Join(
            '\u001F',
            values.Select(value => $"{(int)value.Kind}:{value.Text}"));
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

    private static IReadOnlyList<GraphQuerySchemaEntry> ReadSchemaEntries(
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

#pragma warning disable CA2100, S2077
    private sealed class SqlCompiler
    {
        private readonly SqliteCommand command;
        private readonly ParsedQuery query;
        private int parameterIndex;

        internal SqlCompiler(SqliteConnection connection, ParsedQuery query)
        {
            command = connection.CreateCommand();
            this.query = query;
        }

        internal SqliteCommand Compile(Guid generationId, int matchLimit)
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
            command.Parameters.AddWithValue(
                "$generationId",
                generationId.ToString("D", CultureInfo.InvariantCulture));
            AppendPatternPredicates(sql);

            if (query.Where is not null)
            {
                sql.Append(" AND (").Append(CompileExpression(query.Where)).Append(')');
            }

            sql.Append(" LIMIT $matchLimit;");
            command.Parameters.AddWithValue("$matchLimit", matchLimit);
            command.CommandText = sql.ToString();
            return command;
        }

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

        private void AppendPatternPredicates(StringBuilder sql)
        {
            AppendNodePatternPredicates(sql, query.FirstNode, "n0", "no0");

            if (query.Relationship is RelationshipPattern relationship
                && query.SecondNode is NodePattern secondNode)
            {
                if (!string.IsNullOrWhiteSpace(relationship.TypeName))
                {
                    sql.Append(" AND r0.relationship_type = ")
                        .Append(AddParameter(relationship.TypeName));
                }

                AppendPropertyMapPredicates(sql, relationship.Properties, "r0", isNode: false);
                AppendNodePatternPredicates(sql, secondNode, "n1", "no1");
            }
        }

        private void AppendNodePatternPredicates(
            StringBuilder sql,
            NodePattern node,
            string entityAlias,
            string observationAlias)
        {
            if (!string.IsNullOrWhiteSpace(node.Label))
            {
                string parameter = AddParameter(node.Label);
                sql.Append(" AND (")
                    .Append(entityAlias).Append(".type_name = ").Append(parameter)
                    .Append(" OR EXISTS (SELECT 1 FROM json_each(COALESCE(")
                    .Append(observationAlias).Append(".source_labels_json, '[]')) label WHERE label.value = ")
                    .Append(parameter).Append("))");
            }

            AppendPropertyMapPredicates(sql, node.Properties, entityAlias, isNode: true);
        }

        private void AppendPropertyMapPredicates(
            StringBuilder sql,
            IReadOnlyList<PropertyMapItem> properties,
            string ownerAlias,
            bool isNode)
        {
            foreach (PropertyMapItem property in properties)
            {
                PropertyExpression expression = new(
                    isNode ? query.GetNodeVariable(ownerAlias == "n0" ? 0 : 1) : query.Relationship!.VariableName,
                    property.Name,
                    property.Start,
                    property.Length);
                sql.Append(" AND ").Append(CompileProperty(expression)).Append(" = ")
                    .Append(AddLiteralParameter(property.Value));
            }
        }

        private string CompileExpression(Expression expression)
        {
            return expression switch
            {
                BinaryExpression binary when binary.Operator is "AND" or "OR" =>
                    $"({CompileExpression(binary.Left)} {binary.Operator} {CompileExpression(binary.Right)})",
                UnaryExpression unary => $"NOT ({CompileExpression(unary.Operand)})",
                ComparisonExpression comparison => CompileComparison(comparison),
                NullTestExpression nullTest =>
                    $"({CompileScalar(nullTest.Operand)} IS {(nullTest.IsNegated ? "NOT " : string.Empty)}NULL)",
                InExpression inExpression => CompileIn(inExpression),
                _ => throw new GraphCypherParseException(expression.Start, expression.Length, "This expression is not valid in WHERE."),
            };
        }

        private string CompileComparison(ComparisonExpression comparison)
        {
            string left = CompileScalar(comparison.Left);
            string right = CompileScalar(comparison.Right);
            return comparison.Operator switch
            {
                "CONTAINS" => $"instr(COALESCE({left}, ''), {right}) > 0",
                "STARTS WITH" => $"substr(COALESCE({left}, ''), 1, length({right})) = {right}",
                "ENDS WITH" => $"substr(COALESCE({left}, ''), -length({right})) = {right}",
                _ => $"{left} {comparison.Operator} {right}",
            };
        }

        private string CompileIn(InExpression expression)
        {
            if (expression.Values.Count == 0)
            {
                return "0 = 1";
            }

            string values = string.Join(", ", expression.Values.Select(CompileScalar));
            return $"{CompileScalar(expression.Operand)} IN ({values})";
        }

        private string CompileScalar(Expression expression)
        {
            return expression switch
            {
                LiteralExpression literal => AddLiteralParameter(literal),
                PropertyExpression property => CompileProperty(property),
                FunctionExpression function when function.Name.Equals("toLower", StringComparison.OrdinalIgnoreCase) =>
                    $"lower({CompileScalar(function.Arguments[0])})",
                FunctionExpression function when function.Name.Equals("toUpper", StringComparison.OrdinalIgnoreCase) =>
                    $"upper({CompileScalar(function.Arguments[0])})",
                FunctionExpression function when function.Name.Equals("type", StringComparison.OrdinalIgnoreCase) =>
                    "r0.relationship_type",
                _ => throw new GraphCypherParseException(expression.Start, expression.Length, "This value is not supported in WHERE."),
            };
        }

        private string CompileProperty(PropertyExpression property)
        {
            VariableBinding binding = query.GetBinding(property.VariableName, property.Start, property.Length);
            string sql;

            if (binding.Kind == BindingKind.Node)
            {
                string entityAlias = binding.Index == 0 ? "n0" : "n1";
                string observationAlias = binding.Index == 0 ? "no0" : "no1";
                sql = property.PropertyName switch
                {
                    "canonicalId" => $"{entityAlias}.canonical_id",
                    "displayLabel" => $"{entityAlias}.display_label",
                    "typeName" => $"{entityAlias}.type_name",
                    "kind" => $"CAST({entityAlias}.kind AS TEXT)",
                    "sourceNamespace" => $"{entityAlias}.source_namespace",
                    "firstDiscoveredAt" => $"{entityAlias}.first_discovered_at_utc",
                    "lastUpdatedAt" => $"{entityAlias}.last_updated_at_utc",
                    "degree" => $"""
                        (SELECT COUNT(*) FROM graph_relationships degree_relationship
                         WHERE degree_relationship.generation_id = {entityAlias}.generation_id
                           AND ((degree_relationship.source_kind = {entityAlias}.kind
                             AND degree_relationship.source_type_name = {entityAlias}.type_name
                             AND degree_relationship.source_canonical_id = {entityAlias}.canonical_id
                             AND degree_relationship.source_namespace = {entityAlias}.source_namespace)
                             OR (degree_relationship.target_kind = {entityAlias}.kind
                             AND degree_relationship.target_type_name = {entityAlias}.type_name
                             AND degree_relationship.target_canonical_id = {entityAlias}.canonical_id
                             AND degree_relationship.target_namespace = {entityAlias}.source_namespace)))
                        """,
                    _ => $"json_extract(COALESCE({observationAlias}.properties_json, '{{}}'), {AddJsonPath(property.PropertyName)})",
                };
            }
            else
            {
                sql = property.PropertyName switch
                {
                    "typeName" => "r0.relationship_type",
                    "discriminator" => "r0.discriminator",
                    "firstDiscoveredAt" => "r0.first_discovered_at_utc",
                    "lastUpdatedAt" => "r0.last_updated_at_utc",
                    _ => $"json_extract(COALESCE(ro0.properties_json, '{{}}'), {AddJsonPath(property.PropertyName)})",
                };
            }

            return sql;
        }

        private string AddJsonPath(string propertyName)
        {
            string escapedName = propertyName.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            return AddParameter($"$.\"{escapedName}\"");
        }

        private string AddLiteralParameter(LiteralExpression literal)
        {
            object value = literal.Kind switch
            {
                LiteralKind.Null => DBNull.Value,
                LiteralKind.Number => literal.NumberValue,
                LiteralKind.Boolean => literal.BooleanValue ? "true" : "false",
                _ => literal.TextValue,
            };
            return AddParameter(value);
        }

        private string AddParameter(object? value)
        {
            string name = $"$p{parameterIndex.ToString(CultureInfo.InvariantCulture)}";
            parameterIndex++;
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return name;
        }
    }
#pragma warning restore CA2100, S2077

    private sealed class Parser
    {
        private readonly string queryText;
        private readonly IReadOnlyList<Token> tokens;
        private int index;

        internal Parser(string queryText)
        {
            this.queryText = queryText;
            tokens = new Lexer(queryText).Tokenize();
        }

        internal ParsedQuery Parse()
        {
            ExpectKeyword("MATCH");
            NodePattern firstNode = ParseNode();
            RelationshipPattern? relationship = null;
            NodePattern? secondNode = null;

            if (Current.Kind is TokenKind.Minus or TokenKind.ArrowLeft)
            {
                relationship = ParseRelationship();
                secondNode = ParseNode();
            }

            if (Current.Kind is TokenKind.Minus or TokenKind.ArrowLeft or TokenKind.Comma)
            {
                throw Failure("This release supports one connected relationship pattern per query.");
            }

            Expression? where = null;

            if (MatchKeyword("WHERE"))
            {
                where = ParseOr();
            }

            int returnStart = Current.Start;
            ExpectKeyword("RETURN");
            bool isDistinct = MatchKeyword("DISTINCT");
            List<ReturnItem> returnItems = ParseReturnItems();
            List<OrderItem> orderItems = [];

            if (MatchKeyword("ORDER"))
            {
                ExpectKeyword("BY");
                orderItems = ParseOrderItems();
            }

            int skip = 0;
            int? limit = null;

            if (MatchKeyword("SKIP"))
            {
                skip = ParseBoundedInteger("SKIP", 10_000);
            }

            if (MatchKeyword("LIMIT"))
            {
                limit = ParseBoundedInteger("LIMIT", 1_000);
            }

            Match(TokenKind.Semicolon);

            if (Current.Kind != TokenKind.End)
            {
                string message = IsWriteKeyword(Current.Text)
                    ? $"Graph write clause '{Current.Text}' is not supported. Queries are read-only."
                    : $"Unexpected clause or token '{Current.Text}'.";
                throw Failure(message);
            }

            ParsedQuery parsed = new(
                firstNode,
                relationship,
                secondNode,
                where,
                returnItems,
                orderItems,
                isDistinct,
                skip,
                limit,
                returnStart);
            parsed.ValidateBindings();
            return parsed;
        }

        private static bool IsWriteKeyword(string text)
        {
            return text.Equals("CREATE", StringComparison.OrdinalIgnoreCase)
                || text.Equals("MERGE", StringComparison.OrdinalIgnoreCase)
                || text.Equals("SET", StringComparison.OrdinalIgnoreCase)
                || text.Equals("REMOVE", StringComparison.OrdinalIgnoreCase)
                || text.Equals("DELETE", StringComparison.OrdinalIgnoreCase)
                || text.Equals("DETACH", StringComparison.OrdinalIgnoreCase)
                || text.Equals("DROP", StringComparison.OrdinalIgnoreCase);
        }

        private NodePattern ParseNode()
        {
            Token start = Expect(TokenKind.LeftParenthesis, "Expected '(' to begin a node pattern.");
            string variable = string.Empty;
            string label = string.Empty;

            if (Current.Kind == TokenKind.Identifier && !Current.Text.Equals("WHERE", StringComparison.OrdinalIgnoreCase))
            {
                variable = Advance().Text;
            }

            if (Match(TokenKind.Colon))
            {
                label = ExpectIdentifier("Expected a node label after ':'.").Text;
            }

            IReadOnlyList<PropertyMapItem> properties = Current.Kind == TokenKind.LeftBrace
                ? ParsePropertyMap()
                : [];
            Token end = Expect(TokenKind.RightParenthesis, "Expected ')' to close the node pattern.");
            return new NodePattern(variable, label, properties, start.Start, EndOf(end) - start.Start);
        }

        private RelationshipPattern ParseRelationship()
        {
            Token start = Current;
            bool beginsIncoming = Match(TokenKind.ArrowLeft);

            if (!beginsIncoming)
            {
                Expect(TokenKind.Minus, "Expected '-' before the relationship pattern.");
            }

            string variable = string.Empty;
            string typeName = string.Empty;
            IReadOnlyList<PropertyMapItem> properties = [];

            if (Match(TokenKind.LeftBracket))
            {
                if (Current.Kind == TokenKind.Identifier)
                {
                    variable = Advance().Text;
                }

                if (Match(TokenKind.Colon))
                {
                    typeName = ExpectIdentifier("Expected a relationship type after ':'.").Text;
                }

                if (Match(TokenKind.Star))
                {
                    throw Failure("Variable-length paths are not supported in this release. Use a fixed relationship pattern.");
                }

                properties = Current.Kind == TokenKind.LeftBrace ? ParsePropertyMap() : [];
                Expect(TokenKind.RightBracket, "Expected ']' to close the relationship pattern.");
            }

            RelationshipDirection direction;

            if (beginsIncoming)
            {
                Expect(TokenKind.Minus, "Expected '-' after an incoming relationship pattern.");
                direction = RelationshipDirection.Incoming;
            }
            else if (Match(TokenKind.ArrowRight))
            {
                direction = RelationshipDirection.Outgoing;
            }
            else
            {
                Expect(TokenKind.Minus, "Expected '-' or '->' after the relationship pattern.");
                direction = RelationshipDirection.Undirected;
            }

            return new RelationshipPattern(
                variable,
                typeName,
                direction,
                properties,
                start.Start,
                Current.Start - start.Start);
        }

        private IReadOnlyList<PropertyMapItem> ParsePropertyMap()
        {
            Expect(TokenKind.LeftBrace, "Expected '{' to begin a property map.");
            List<PropertyMapItem> properties = [];

            if (!Match(TokenKind.RightBrace))
            {
                do
                {
                    Token name = ExpectIdentifier("Expected a property name.");
                    Expect(TokenKind.Colon, "Expected ':' after the property name.");
                    LiteralExpression value = ParseLiteral();
                    properties.Add(new PropertyMapItem(
                        name.Text,
                        value,
                        name.Start,
                        EndOf(Previous) - name.Start));
                }
                while (Match(TokenKind.Comma));

                Expect(TokenKind.RightBrace, "Expected '}' to close the property map.");
            }

            return properties.AsReadOnly();
        }

        private List<ReturnItem> ParseReturnItems()
        {
            List<ReturnItem> items = [];

            do
            {
                int start = Current.Start;
                Expression expression = ParseValueExpression(allowStar: true);
                int end = EndOf(Previous);
                string? alias = MatchKeyword("AS")
                    ? ExpectIdentifier("Expected a column alias after AS.").Text
                    : null;
                items.Add(new ReturnItem(
                    expression,
                    alias,
                    queryText[start..end].Trim()));
            }
            while (Match(TokenKind.Comma));

            if (items.Count == 0)
            {
                throw Failure("RETURN must project at least one expression.");
            }

            return items;
        }

        private List<OrderItem> ParseOrderItems()
        {
            List<OrderItem> items = [];

            do
            {
                Expression expression = ParseValueExpression(allowStar: false);
                bool descending = MatchKeyword("DESC");

                if (!descending)
                {
                    MatchKeyword("ASC");
                }

                items.Add(new OrderItem(expression, descending));
            }
            while (Match(TokenKind.Comma));

            return items;
        }

        private Expression ParseOr()
        {
            Expression expression = ParseAnd();

            while (MatchKeyword("OR"))
            {
                expression = new BinaryExpression(expression, "OR", ParseAnd());
            }

            return expression;
        }

        private Expression ParseAnd()
        {
            Expression expression = ParseNot();

            while (MatchKeyword("AND"))
            {
                expression = new BinaryExpression(expression, "AND", ParseNot());
            }

            return expression;
        }

        private Expression ParseNot()
        {
            if (MatchKeyword("NOT"))
            {
                return new UnaryExpression(ParseNot());
            }

            if (Match(TokenKind.LeftParenthesis))
            {
                Expression nested = ParseOr();
                Expect(TokenKind.RightParenthesis, "Expected ')' after the filter expression.");
                return nested;
            }

            return ParseComparison();
        }

        private Expression ParseComparison()
        {
            Expression left = ParseValueExpression(allowStar: false);

            if (MatchKeyword("IS"))
            {
                bool negated = MatchKeyword("NOT");
                ExpectKeyword("NULL");
                return new NullTestExpression(left, negated);
            }

            if (MatchKeyword("IN"))
            {
                Expect(TokenKind.LeftBracket, "Expected '[' after IN.");
                List<Expression> values = [];

                if (!Match(TokenKind.RightBracket))
                {
                    do
                    {
                        values.Add(ParseValueExpression(allowStar: false));
                    }
                    while (Match(TokenKind.Comma));

                    Expect(TokenKind.RightBracket, "Expected ']' after the IN values.");
                }

                return new InExpression(left, values);
            }

            string? comparisonOperator = ParseComparisonOperator();

            if (comparisonOperator is null)
            {
                throw Failure("Expected a comparison operator in WHERE.");
            }

            return new ComparisonExpression(
                left,
                comparisonOperator,
                ParseValueExpression(allowStar: false));
        }

        private string? ParseComparisonOperator()
        {
            string? value = Current.Kind switch
            {
                TokenKind.Equals => "=",
                TokenKind.NotEquals => "<>",
                TokenKind.LessThan => "<",
                TokenKind.LessThanOrEqual => "<=",
                TokenKind.GreaterThan => ">",
                TokenKind.GreaterThanOrEqual => ">=",
                _ => null,
            };

            if (value is not null)
            {
                Advance();
            }
            else if (MatchKeyword("CONTAINS"))
            {
                value = "CONTAINS";
            }
            else if (MatchKeyword("STARTS"))
            {
                ExpectKeyword("WITH");
                value = "STARTS WITH";
            }
            else if (MatchKeyword("ENDS"))
            {
                ExpectKeyword("WITH");
                value = "ENDS WITH";
            }

            return value;
        }

        private Expression ParseValueExpression(bool allowStar)
        {
            Token token = Current;

            if (token.Kind is TokenKind.StringLiteral or TokenKind.Number
                || IsKeyword("TRUE") || IsKeyword("FALSE") || IsKeyword("NULL"))
            {
                return ParseLiteral();
            }

            if (token.Kind != TokenKind.Identifier)
            {
                throw Failure("Expected a variable, property, function, or literal value.");
            }

            Advance();

            if (Match(TokenKind.LeftParenthesis))
            {
                bool distinct = MatchKeyword("DISTINCT");
                bool star = allowStar && Match(TokenKind.Star);
                List<Expression> arguments = [];

                if (!star && !Match(TokenKind.RightParenthesis))
                {
                    do
                    {
                        arguments.Add(ParseValueExpression(allowStar: false));
                    }
                    while (Match(TokenKind.Comma));

                    Expect(TokenKind.RightParenthesis, "Expected ')' after function arguments.");
                }
                else if (star)
                {
                    Expect(TokenKind.RightParenthesis, "Expected ')' after '*'.");
                }

                ValidateFunction(token, arguments, star);
                return new FunctionExpression(
                    token.Text,
                    arguments,
                    star,
                    distinct,
                    token.Start,
                    EndOf(Previous) - token.Start);
            }

            if (Match(TokenKind.Dot))
            {
                Token property = ExpectIdentifier("Expected a property name after '.'.");
                return new PropertyExpression(
                    token.Text,
                    property.Text,
                    token.Start,
                    EndOf(property) - token.Start);
            }

            return new VariableExpression(token.Text, token.Start, token.Length);
        }

        private static void ValidateFunction(Token token, IReadOnlyList<Expression> arguments, bool star)
        {
            string name = token.Text.ToUpperInvariant();
            bool valid = name switch
            {
                "COUNT" => star || arguments.Count == 1,
                "TYPE" or "LABELS" or "ELEMENTID" or "PROPERTIES" or "TOLOWER" or "TOUPPER" =>
                    !star && arguments.Count == 1,
                _ => false,
            };

            if (!valid)
            {
                throw new GraphCypherParseException(
                    token.Start,
                    token.Length,
                    $"Function '{token.Text}' is not supported or has invalid arguments.");
            }
        }

        private LiteralExpression ParseLiteral()
        {
            Token token = Advance();

            if (token.Kind == TokenKind.StringLiteral)
            {
                return LiteralExpression.FromText(token.Text, token.Start, token.Length);
            }

            if (token.Kind == TokenKind.Number
                && double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                return LiteralExpression.FromNumber(number, token.Start, token.Length);
            }

            if (token.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                || token.Text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            {
                return LiteralExpression.FromBoolean(
                    token.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase),
                    token.Start,
                    token.Length);
            }

            if (token.Text.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return LiteralExpression.FromNull(token.Start, token.Length);
            }

            throw new GraphCypherParseException(token.Start, token.Length, "Expected a string, number, Boolean, or null literal.");
        }

        private int ParseBoundedInteger(string clauseName, int maximum)
        {
            Token token = Expect(TokenKind.Number, $"Expected an integer after {clauseName}.");

            if (!int.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                || value < 0
                || value > maximum)
            {
                throw new GraphCypherParseException(
                    token.Start,
                    token.Length,
                    $"{clauseName} must be an integer from 0 to {maximum:N0}.");
            }

            return value;
        }

        private void ExpectKeyword(string keyword)
        {
            if (!MatchKeyword(keyword))
            {
                throw Failure($"Expected {keyword}.");
            }
        }

        private bool MatchKeyword(string keyword)
        {
            bool matches = IsKeyword(keyword);

            if (matches)
            {
                Advance();
            }

            return matches;
        }

        private bool IsKeyword(string keyword)
        {
            return Current.Kind == TokenKind.Identifier
                && Current.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private Token ExpectIdentifier(string message) => Expect(TokenKind.Identifier, message);

        private Token Expect(TokenKind kind, string message)
        {
            if (Current.Kind != kind)
            {
                throw Failure(message);
            }

            return Advance();
        }

        private bool Match(TokenKind kind)
        {
            bool matches = Current.Kind == kind;

            if (matches)
            {
                Advance();
            }

            return matches;
        }

        private Token Advance()
        {
            Token token = Current;

            if (index < tokens.Count - 1)
            {
                index++;
            }

            return token;
        }

        private GraphCypherParseException Failure(string message)
        {
            return new GraphCypherParseException(Current.Start, Math.Max(Current.Length, 1), message);
        }

        private Token Current => tokens[index];

        private Token Previous => tokens[Math.Max(0, index - 1)];

        private static int EndOf(Token token) => token.Start + token.Length;
    }

    private sealed class Lexer
    {
        private readonly string text;
        private int position;

        internal Lexer(string text)
        {
            this.text = text;
        }

        internal IReadOnlyList<Token> Tokenize()
        {
            List<Token> tokens = [];

            while (true)
            {
                SkipTrivia();

                if (position >= text.Length)
                {
                    tokens.Add(new Token(TokenKind.End, string.Empty, position, 0));
                    break;
                }

                tokens.Add(ReadToken());
            }

            return tokens.AsReadOnly();
        }

        private Token ReadToken()
        {
            int start = position;
            char character = text[position++];
            return character switch
            {
                '(' => Create(TokenKind.LeftParenthesis, start),
                ')' => Create(TokenKind.RightParenthesis, start),
                '[' => Create(TokenKind.LeftBracket, start),
                ']' => Create(TokenKind.RightBracket, start),
                '{' => Create(TokenKind.LeftBrace, start),
                '}' => Create(TokenKind.RightBrace, start),
                ':' => Create(TokenKind.Colon, start),
                ',' => Create(TokenKind.Comma, start),
                '.' when position < text.Length && char.IsDigit(text[position]) => ReadNumber(start),
                '.' => Create(TokenKind.Dot, start),
                ';' => Create(TokenKind.Semicolon, start),
                '*' => Create(TokenKind.Star, start),
                '=' => Create(TokenKind.Equals, start),
                '!' when Match('=') => Create(TokenKind.NotEquals, start),
                '!' => throw new GraphCypherParseException(start, 1, "Expected '=' after '!'."),
                '-' when Match('>') => Create(TokenKind.ArrowRight, start),
                '-' => Create(TokenKind.Minus, start),
                '<' when Match('-') => Create(TokenKind.ArrowLeft, start),
                '<' when Match('=') => Create(TokenKind.LessThanOrEqual, start),
                '<' when Match('>') => Create(TokenKind.NotEquals, start),
                '<' => Create(TokenKind.LessThan, start),
                '>' when Match('=') => Create(TokenKind.GreaterThanOrEqual, start),
                '>' => Create(TokenKind.GreaterThan, start),
                '\'' => ReadString(start),
                '`' => ReadQuotedIdentifier(start),
                _ when char.IsLetter(character) || character == '_' => ReadIdentifier(start),
                _ when char.IsDigit(character) => ReadNumber(start),
                _ => throw new GraphCypherParseException(start, 1, $"Unexpected character '{character}'."),
            };
        }

        private Token ReadIdentifier(int start)
        {
            while (position < text.Length
                && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
            {
                position++;
            }

            return Create(TokenKind.Identifier, start);
        }

        private Token ReadQuotedIdentifier(int start)
        {
            StringBuilder value = new();

            while (position < text.Length)
            {
                char character = text[position++];

                if (character == '`')
                {
                    if (position < text.Length && text[position] == '`')
                    {
                        position++;
                        value.Append('`');
                    }
                    else
                    {
                        return new Token(TokenKind.Identifier, value.ToString(), start, position - start);
                    }
                }
                else
                {
                    value.Append(character);
                }
            }

            throw new GraphCypherParseException(start, text.Length - start, "Unterminated quoted identifier.");
        }

        private Token ReadString(int start)
        {
            StringBuilder value = new();

            while (position < text.Length)
            {
                char character = text[position++];

                if (character == '\'')
                {
                    if (position < text.Length && text[position] == '\'')
                    {
                        position++;
                        value.Append('\'');
                    }
                    else
                    {
                        return new Token(TokenKind.StringLiteral, value.ToString(), start, position - start);
                    }
                }
                else if (character == '\\' && position < text.Length)
                {
                    value.Append(ReadEscape(text[position++]));
                }
                else
                {
                    value.Append(character);
                }
            }

            throw new GraphCypherParseException(start, text.Length - start, "Unterminated string literal.");
        }

        private static char ReadEscape(char character)
        {
            return character switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => character,
            };
        }

        private Token ReadNumber(int start)
        {
            while (position < text.Length && char.IsDigit(text[position]))
            {
                position++;
            }

            if (position < text.Length && text[position] == '.')
            {
                position++;

                while (position < text.Length && char.IsDigit(text[position]))
                {
                    position++;
                }
            }

            return Create(TokenKind.Number, start);
        }

        private void SkipTrivia()
        {
            bool skipped;

            do
            {
                skipped = false;

                while (position < text.Length && char.IsWhiteSpace(text[position]))
                {
                    position++;
                    skipped = true;
                }

                if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '/')
                {
                    position += 2;

                    while (position < text.Length && text[position] is not '\r' and not '\n')
                    {
                        position++;
                    }

                    skipped = true;
                }
                else if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*')
                {
                    int start = position;
                    position += 2;

                    while (position + 1 < text.Length
                        && (text[position] != '*' || text[position + 1] != '/'))
                    {
                        position++;
                    }

                    if (position + 1 >= text.Length)
                    {
                        throw new GraphCypherParseException(start, text.Length - start, "Unterminated block comment.");
                    }

                    position += 2;
                    skipped = true;
                }
            }
            while (skipped);
        }

        private bool Match(char expected)
        {
            bool matches = position < text.Length && text[position] == expected;

            if (matches)
            {
                position++;
            }

            return matches;
        }

        private Token Create(TokenKind kind, int start)
        {
            return new Token(kind, text[start..position], start, position - start);
        }
    }

    private sealed class ParsedQuery
    {
        private readonly Dictionary<string, VariableBinding> bindings = new(StringComparer.Ordinal);

        internal ParsedQuery(
            NodePattern firstNode,
            RelationshipPattern? relationship,
            NodePattern? secondNode,
            Expression? where,
            IReadOnlyList<ReturnItem> returnItems,
            IReadOnlyList<OrderItem> orderItems,
            bool isDistinct,
            int skip,
            int? limit,
            int returnStart)
        {
            FirstNode = firstNode;
            Relationship = relationship;
            SecondNode = secondNode;
            Where = where;
            ReturnItems = returnItems;
            OrderItems = orderItems;
            IsDistinct = isDistinct;
            Skip = skip;
            Limit = limit;
            ReturnStart = returnStart;
        }

        internal NodePattern FirstNode { get; }

        internal RelationshipPattern? Relationship { get; }

        internal NodePattern? SecondNode { get; }

        internal Expression? Where { get; }

        internal IReadOnlyList<ReturnItem> ReturnItems { get; }

        internal IReadOnlyList<OrderItem> OrderItems { get; }

        internal bool IsDistinct { get; }

        internal int Skip { get; }

        internal int? Limit { get; }

        internal int ReturnStart { get; }

        internal void ValidateBindings()
        {
            AddBinding(FirstNode.VariableName, new VariableBinding(BindingKind.Node, 0), FirstNode);

            if (SecondNode is not null)
            {
                AddBinding(SecondNode.VariableName, new VariableBinding(BindingKind.Node, 1), SecondNode);
            }

            if (Relationship is not null)
            {
                AddBinding(
                    Relationship.VariableName,
                    new VariableBinding(BindingKind.Relationship, 0),
                    Relationship);
            }

            IEnumerable<Expression> expressions = ReturnItems.Select(item => item.Expression)
                .Concat(OrderItems.Select(item => item.Expression));

            if (Where is not null)
            {
                expressions = expressions.Append(Where);
            }

            foreach (Expression expression in expressions)
            {
                ValidateExpression(expression);
            }
        }

        internal VariableBinding GetBinding(string variableName, int start, int length)
        {
            return bindings.TryGetValue(variableName, out VariableBinding binding)
                ? binding
                : throw new GraphCypherParseException(start, length, $"Variable '{variableName}' is not defined by MATCH.");
        }

        internal string GetNodeVariable(int nodeIndex)
        {
            NodePattern node = nodeIndex == 0 ? FirstNode : SecondNode!;
            return node.VariableName;
        }

        private void AddBinding(
            string variableName,
            VariableBinding binding,
            PatternElement pattern)
        {
            if (!string.IsNullOrWhiteSpace(variableName)
                && !bindings.TryAdd(variableName, binding))
            {
                throw new GraphCypherParseException(
                    pattern.Start,
                    pattern.Length,
                    $"Variable '{variableName}' is declared more than once.");
            }
        }

        private void ValidateExpression(Expression expression)
        {
            switch (expression)
            {
                case VariableExpression variable:
                    GetBinding(variable.Name, variable.Start, variable.Length);
                    break;
                case PropertyExpression property:
                    GetBinding(property.VariableName, property.Start, property.Length);
                    break;
                case FunctionExpression function:
                    foreach (Expression argument in function.Arguments)
                    {
                        ValidateExpression(argument);
                    }

                    break;
                case BinaryExpression binary:
                    ValidateExpression(binary.Left);
                    ValidateExpression(binary.Right);
                    break;
                case UnaryExpression unary:
                    ValidateExpression(unary.Operand);
                    break;
                case ComparisonExpression comparison:
                    ValidateExpression(comparison.Left);
                    ValidateExpression(comparison.Right);
                    break;
                case NullTestExpression nullTest:
                    ValidateExpression(nullTest.Operand);
                    break;
                case InExpression inExpression:
                    ValidateExpression(inExpression.Operand);

                    foreach (Expression value in inExpression.Values)
                    {
                        ValidateExpression(value);
                    }

                    break;
            }
        }
    }

    private sealed class MatchRow
    {
        private readonly ParsedQuery query;

        internal MatchRow(
            ParsedQuery query,
            IReadOnlyList<BoundNode> nodes,
            BoundRelationship? relationship)
        {
            this.query = query;
            Nodes = nodes;
            Relationship = relationship;
        }

        internal IReadOnlyList<BoundNode> Nodes { get; }

        internal BoundRelationship? Relationship { get; }

        internal EvalValue GetVariable(string variableName)
        {
            VariableBinding binding = query.GetBinding(variableName, 0, Math.Max(variableName.Length, 1));
            return binding.Kind == BindingKind.Node
                ? EvalValue.FromNode(Nodes[binding.Index])
                : Relationship is null ? EvalValue.Null : EvalValue.FromRelationship(Relationship);
        }
    }

    private abstract class PatternElement
    {
        protected PatternElement(string variableName, int start, int length)
        {
            VariableName = variableName;
            Start = start;
            Length = length;
        }

        internal string VariableName { get; }

        internal int Start { get; }

        internal int Length { get; }
    }

    private sealed class NodePattern : PatternElement
    {
        internal NodePattern(
            string variableName,
            string label,
            IReadOnlyList<PropertyMapItem> properties,
            int start,
            int length)
            : base(variableName, start, length)
        {
            Label = label;
            Properties = properties;
        }

        internal string Label { get; }

        internal IReadOnlyList<PropertyMapItem> Properties { get; }
    }

    private sealed class RelationshipPattern : PatternElement
    {
        internal RelationshipPattern(
            string variableName,
            string typeName,
            RelationshipDirection direction,
            IReadOnlyList<PropertyMapItem> properties,
            int start,
            int length)
            : base(variableName, start, length)
        {
            TypeName = typeName;
            Direction = direction;
            Properties = properties;
        }

        internal string TypeName { get; }

        internal RelationshipDirection Direction { get; }

        internal IReadOnlyList<PropertyMapItem> Properties { get; }
    }

    private abstract class Expression
    {
        protected Expression(int start, int length)
        {
            Start = start;
            Length = length;
        }

        internal int Start { get; }

        internal int Length { get; }
    }

    private sealed class VariableExpression : Expression
    {
        internal VariableExpression(string name, int start, int length)
            : base(start, length)
        {
            Name = name;
        }

        internal string Name { get; }
    }

    private sealed class PropertyExpression : Expression
    {
        internal PropertyExpression(string variableName, string propertyName, int start, int length)
            : base(start, length)
        {
            VariableName = variableName;
            PropertyName = propertyName;
        }

        internal string VariableName { get; }

        internal string PropertyName { get; }
    }

    private sealed class LiteralExpression : Expression
    {
        private LiteralExpression(
            LiteralKind kind,
            string textValue,
            double numberValue,
            bool booleanValue,
            int start,
            int length)
            : base(start, length)
        {
            Kind = kind;
            TextValue = textValue;
            NumberValue = numberValue;
            BooleanValue = booleanValue;
        }

        internal LiteralKind Kind { get; }

        internal string TextValue { get; }

        internal double NumberValue { get; }

        internal bool BooleanValue { get; }

        internal static LiteralExpression FromText(string value, int start, int length)
        {
            return new LiteralExpression(LiteralKind.Text, value, 0, false, start, length);
        }

        internal static LiteralExpression FromNumber(double value, int start, int length)
        {
            return new LiteralExpression(LiteralKind.Number, string.Empty, value, false, start, length);
        }

        internal static LiteralExpression FromBoolean(bool value, int start, int length)
        {
            return new LiteralExpression(LiteralKind.Boolean, string.Empty, 0, value, start, length);
        }

        internal static LiteralExpression FromNull(int start, int length)
        {
            return new LiteralExpression(LiteralKind.Null, string.Empty, 0, false, start, length);
        }
    }

    private sealed class FunctionExpression : Expression
    {
        internal FunctionExpression(
            string name,
            IReadOnlyList<Expression> arguments,
            bool isStarArgument,
            bool isDistinct,
            int start,
            int length)
            : base(start, length)
        {
            Name = name;
            Arguments = arguments;
            IsStarArgument = isStarArgument;
            IsDistinct = isDistinct;
        }

        internal string Name { get; }

        internal IReadOnlyList<Expression> Arguments { get; }

        internal bool IsStarArgument { get; }

        internal bool IsDistinct { get; }
    }

    private sealed class BinaryExpression : Expression
    {
        internal BinaryExpression(Expression left, string @operator, Expression right)
            : base(left.Start, (right.Start + right.Length) - left.Start)
        {
            Left = left;
            Operator = @operator;
            Right = right;
        }

        internal Expression Left { get; }

        internal string Operator { get; }

        internal Expression Right { get; }
    }

    private sealed class UnaryExpression : Expression
    {
        internal UnaryExpression(Expression operand)
            : base(operand.Start, operand.Length)
        {
            Operand = operand;
        }

        internal Expression Operand { get; }
    }

    private sealed class ComparisonExpression : Expression
    {
        internal ComparisonExpression(Expression left, string @operator, Expression right)
            : base(left.Start, (right.Start + right.Length) - left.Start)
        {
            Left = left;
            Operator = @operator;
            Right = right;
        }

        internal Expression Left { get; }

        internal string Operator { get; }

        internal Expression Right { get; }
    }

    private sealed class NullTestExpression : Expression
    {
        internal NullTestExpression(Expression operand, bool isNegated)
            : base(operand.Start, operand.Length)
        {
            Operand = operand;
            IsNegated = isNegated;
        }

        internal Expression Operand { get; }

        internal bool IsNegated { get; }
    }

    private sealed class InExpression : Expression
    {
        internal InExpression(Expression operand, IReadOnlyList<Expression> values)
            : base(operand.Start, operand.Length)
        {
            Operand = operand;
            Values = values;
        }

        internal Expression Operand { get; }

        internal IReadOnlyList<Expression> Values { get; }
    }

    private sealed class GraphCypherParseException : Exception
    {
        internal GraphCypherParseException(int start, int length, string message)
            : base(message)
        {
            Start = Math.Max(0, start);
            Length = Math.Max(1, length);
        }

        internal int Start { get; }

        internal int Length { get; }
    }

    private sealed record PropertyMapItem(
        string Name,
        LiteralExpression Value,
        int Start,
        int Length);

    private sealed record ReturnItem(Expression Expression, string? Alias, string ExpressionText);

    private sealed record OrderItem(Expression Expression, bool IsDescending);

    private sealed record Projection(
        IReadOnlyList<GraphQueryColumn> Columns,
        IReadOnlyList<GraphQueryRow> Rows,
        IReadOnlyList<MatchRow> ViewportMatches,
        bool AreRowsTruncated);

    private sealed record ProjectedMatch(
        MatchRow Match,
        IReadOnlyList<EvalValue> Values,
        IReadOnlyList<EvalValue> OrderValues);

    private sealed record BoundNode(
        GraphEntitySummary Summary,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyList<string> Labels,
        string PropertiesJson);

    private sealed record BoundRelationship(
        GraphRelationshipKey Key,
        DateTimeOffset FirstDiscoveredAtUtc,
        DateTimeOffset LastUpdatedAtUtc,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyList<string> Labels,
        string PropertiesJson);

    private sealed class EvalValue
    {
        private EvalValue(
            EvalKind kind,
            string text,
            double? number,
            bool? boolean,
            BoundNode? node,
            BoundRelationship? relationship,
            bool isWholeNumber)
        {
            Kind = kind;
            Text = text;
            Number = number;
            Boolean = boolean;
            Node = node;
            Relationship = relationship;
            IsWholeNumber = isWholeNumber;
        }

        internal static EvalValue Null { get; } = new(
            EvalKind.Null,
            string.Empty,
            null,
            null,
            null,
            null,
            false);

        internal EvalKind Kind { get; }

        internal string Text { get; }

        internal double? Number { get; }

        internal bool? Boolean { get; }

        internal BoundNode? Node { get; }

        internal BoundRelationship? Relationship { get; }

        internal bool IsWholeNumber { get; }

        internal static EvalValue FromLiteral(LiteralExpression literal)
        {
            return literal.Kind switch
            {
                LiteralKind.Null => Null,
                LiteralKind.Number => FromNumber(literal.NumberValue),
                LiteralKind.Boolean => FromBoolean(literal.BooleanValue),
                _ => FromText(literal.TextValue),
            };
        }

        internal static EvalValue FromText(string value)
        {
            return new EvalValue(EvalKind.Text, value, null, null, null, null, false);
        }

        internal static EvalValue FromNumber(double value)
        {
            bool isWhole = value >= long.MinValue
                && value <= long.MaxValue
                && Math.Abs(value - Math.Round(value)) < 0.0000000001;
            return new EvalValue(
                EvalKind.Number,
                value.ToString("R", CultureInfo.InvariantCulture),
                value,
                null,
                null,
                null,
                isWhole);
        }

        internal static EvalValue FromBoolean(bool value)
        {
            return new EvalValue(EvalKind.Boolean, value ? "true" : "false", null, value, null, null, false);
        }

        internal static EvalValue FromNode(BoundNode value)
        {
            return new EvalValue(
                EvalKind.Node,
                value.Summary.Entity.ToString(),
                null,
                null,
                value,
                null,
                false);
        }

        internal static EvalValue FromRelationship(BoundRelationship value)
        {
            return new EvalValue(
                EvalKind.Relationship,
                value.Key.ToString(),
                null,
                null,
                null,
                value,
                false);
        }
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Start, int Length);

    private readonly record struct VariableBinding(BindingKind Kind, int Index);

    private enum RelationshipDirection
    {
        Outgoing,
        Incoming,
        Undirected,
    }

    private enum BindingKind
    {
        Node,
        Relationship,
    }

    private enum LiteralKind
    {
        Null,
        Text,
        Number,
        Boolean,
    }

    private enum EvalKind
    {
        Null,
        Text,
        Number,
        Boolean,
        Node,
        Relationship,
    }

    private enum TokenKind
    {
        End,
        Identifier,
        StringLiteral,
        Number,
        LeftParenthesis,
        RightParenthesis,
        LeftBracket,
        RightBracket,
        LeftBrace,
        RightBrace,
        Colon,
        Comma,
        Dot,
        Semicolon,
        Star,
        Minus,
        ArrowRight,
        ArrowLeft,
        Equals,
        NotEquals,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
    }
}

#pragma warning restore SA1118, SA1201, SA1204
#pragma warning restore S134, S3267, S3358, S3776
#pragma warning restore S3871
#pragma warning restore CA1859
