using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

#pragma warning disable SA1600, SA1602

namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Executes the backend-neutral portion of the bounded read-only openCypher subset.
/// </summary>
internal static class GraphCypherQueryEngine
{
    private const int MaximumScannedMatchCount = 10_000;

    internal enum EvalKind
    {
        Null,
        Text,
        Number,
        Boolean,
        Node,
        Relationship,
    }

    /// <summary>
    /// Parses, evaluates, projects, and bounds a graph query over backend-provided matches.
    /// </summary>
    /// <param name="request">The pinned query and result bounds.</param>
    /// <param name="readMatches">Reads structural matches for the parsed query.</param>
    /// <param name="cancellationToken">A token that cancels match materialization.</param>
    /// <returns>The projected rows, matched viewport, and diagnostics.</returns>
    internal static GraphQueryResult Execute(
        GraphQueryRequest request,
        Func<ParsedQuery, CancellationToken, IEnumerable<MatchRow>> readMatches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readMatches);
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            ParsedQuery query = new GraphCypherParser(request.QueryText).Parse();
            List<MatchRow> matches = [];

            foreach (MatchRow match in readMatches(query, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Matches(query, match))
                {
                    continue;
                }

                matches.Add(match);
                if (matches.Count > MaximumScannedMatchCount)
                {
                    break;
                }
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
    /// Writes properties in the same deterministic representation used by persisted graph rows.
    /// </summary>
    /// <param name="properties">The normalized graph properties.</param>
    /// <returns>A compact JSON object.</returns>
    internal static string WriteProperties(IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();

            foreach (KeyValuePair<string, string> property in properties.OrderBy(
                property => property.Key,
                StringComparer.Ordinal))
            {
                writer.WriteString(property.Key, property.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool Matches(ParsedQuery query, MatchRow match)
    {
        if (match.Nodes.Count == 0 || !MatchesNode(query.FirstNode, match.Nodes[0]))
        {
            return false;
        }

        if (query.Relationship is RelationshipPattern relationship
            && (query.SecondNode is not NodePattern secondNode
                || match.Nodes.Count < 2
                || match.Relationship is not BoundRelationship boundRelationship
                || !MatchesNode(secondNode, match.Nodes[1])
                || !MatchesRelationship(relationship, boundRelationship)))
        {
            return false;
        }

        return query.Where is null || EvaluatePredicate(query.Where, match) == true;
    }

    private static bool MatchesNode(NodePattern pattern, BoundNode node)
    {
        bool labelMatches = string.IsNullOrWhiteSpace(pattern.Label)
            || string.Equals(node.Summary.Entity.TypeName, pattern.Label, StringComparison.Ordinal)
            || node.Labels.Contains(pattern.Label, StringComparer.Ordinal);
        return labelMatches && MatchesProperties(pattern.Properties, node, null);
    }

    private static bool MatchesRelationship(
        RelationshipPattern pattern,
        BoundRelationship relationship)
    {
        return (string.IsNullOrWhiteSpace(pattern.TypeName)
                || string.Equals(relationship.Key.TypeName, pattern.TypeName, StringComparison.Ordinal))
            && MatchesProperties(pattern.Properties, null, relationship);
    }

    private static bool MatchesProperties(
        IReadOnlyList<PropertyMapItem> properties,
        BoundNode? node,
        BoundRelationship? relationship)
    {
        foreach (PropertyMapItem property in properties)
        {
            EvalValue actual = node is not null
                ? EvaluateNodeProperty(property.Name, node, forFilter: true)
                : EvaluateRelationshipProperty(property.Name, relationship!, forFilter: true);
            EvalValue expected = EvalValue.FromFilterLiteral(property.Value);

            if (actual.Kind == EvalKind.Null
                || expected.Kind == EvalKind.Null
                || !ValuesEqual(actual, expected))
            {
                return false;
            }
        }

        return true;
    }

    private static bool? EvaluatePredicate(Expression expression, MatchRow match)
    {
        return expression switch
        {
            BinaryExpression binary when binary.Operator == "AND" =>
                And(EvaluatePredicate(binary.Left, match), EvaluatePredicate(binary.Right, match)),
            BinaryExpression binary when binary.Operator == "OR" =>
                Or(EvaluatePredicate(binary.Left, match), EvaluatePredicate(binary.Right, match)),
            UnaryExpression unary => Negate(EvaluatePredicate(unary.Operand, match)),
            ComparisonExpression comparison => EvaluateComparison(comparison, match),
            NullTestExpression nullTest =>
                (EvaluateFilterValue(nullTest.Operand, match).Kind == EvalKind.Null) != nullTest.IsNegated,
            InExpression inExpression => EvaluateIn(inExpression, match),
            _ => throw new GraphCypherParseException(
                expression.Start,
                expression.Length,
                "This expression is not valid in WHERE."),
        };
    }

    private static bool? EvaluateComparison(ComparisonExpression comparison, MatchRow match)
    {
        EvalValue left = EvaluateFilterValue(comparison.Left, match);
        EvalValue right = EvaluateFilterValue(comparison.Right, match);
        if (left.Kind == EvalKind.Null || right.Kind == EvalKind.Null)
        {
            return null;
        }

        return comparison.Operator switch
        {
            "=" => ValuesEqual(left, right),
            "<>" => !ValuesEqual(left, right),
            "<" => CompareValues(left, right) < 0,
            "<=" => CompareValues(left, right) <= 0,
            ">" => CompareValues(left, right) > 0,
            ">=" => CompareValues(left, right) >= 0,
            "CONTAINS" => left.Text.Contains(right.Text, StringComparison.Ordinal),
            "STARTS WITH" => left.Text.StartsWith(right.Text, StringComparison.Ordinal),
            "ENDS WITH" => left.Text.EndsWith(right.Text, StringComparison.Ordinal),
            _ => throw new GraphCypherParseException(
                comparison.Start,
                comparison.Length,
                "This comparison is not supported in WHERE."),
        };
    }

    private static bool? EvaluateIn(InExpression expression, MatchRow match)
    {
        EvalValue operand = EvaluateFilterValue(expression.Operand, match);
        if (operand.Kind == EvalKind.Null)
        {
            return null;
        }

        bool containsNull = false;
        foreach (Expression valueExpression in expression.Values)
        {
            EvalValue value = EvaluateFilterValue(valueExpression, match);
            if (value.Kind == EvalKind.Null)
            {
                containsNull = true;
            }
            else if (ValuesEqual(operand, value))
            {
                return true;
            }
        }

        return containsNull ? null : false;
    }

    private static bool? And(bool? left, bool? right)
    {
        if (left == false || right == false)
        {
            return false;
        }

        return left == true && right == true ? true : null;
    }

    private static bool? Or(bool? left, bool? right)
    {
        if (left == true || right == true)
        {
            return true;
        }

        return left == false && right == false ? false : null;
    }

    private static bool? Negate(bool? value) => value.HasValue ? !value.Value : null;

#pragma warning disable S1244 // openCypher equality compares numeric values exactly.
    private static bool ValuesEqual(EvalValue left, EvalValue right)
    {
        if (left.Number is double leftNumber && right.Number is double rightNumber)
        {
            return leftNumber.Equals(rightNumber);
        }

        return string.Equals(left.Text, right.Text, StringComparison.Ordinal);
    }
#pragma warning restore S1244

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
                isTruncated |= !TryAddEntity(entities, node.Summary, maximumEntityCount);
            }

            if (match.Relationship is BoundRelationship relationship
                && !TryAddRelationship(
                    relationships,
                    entities,
                    relationship.Key,
                    maximumRelationshipCount))
            {
                isTruncated = true;
            }
        }

        GraphEntityKey? center = entities.Count == 0 ? null : entities.Keys.First();
        return new GraphViewport(center, entities.Values, relationships, isTruncated);
    }

    private static bool TryAddEntity(
        Dictionary<GraphEntityKey, GraphEntitySummary> entities,
        GraphEntitySummary entity,
        int maximumEntityCount)
    {
        if (entities.ContainsKey(entity.Entity))
        {
            return true;
        }

        if (entities.Count >= maximumEntityCount)
        {
            return false;
        }

        entities.Add(entity.Entity, entity);
        return true;
    }

    private static bool TryAddRelationship(
        List<GraphRelationshipKey> relationships,
        Dictionary<GraphEntityKey, GraphEntitySummary> entities,
        GraphRelationshipKey relationship,
        int maximumRelationshipCount)
    {
        bool shouldInclude = entities.ContainsKey(relationship.Source)
            && entities.ContainsKey(relationship.Target)
            && !relationships.Contains(relationship);
        if (!shouldInclude)
        {
            return true;
        }

        if (relationships.Count >= maximumRelationshipCount)
        {
            return false;
        }

        relationships.Add(relationship);
        return true;
    }

    private static Projection Project(
        ParsedQuery query,
        IReadOnlyList<MatchRow> matches,
        int maximumRowCount)
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
            if (left.Kind == right.Kind)
            {
                comparison = 0;
            }
            else
            {
                comparison = left.Kind == EvalKind.Null ? -1 : 1;
            }
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
            PropertyExpression property => EvaluateProperty(property, match, forFilter: false),
            FunctionExpression function => EvaluateFunction(function, match, forFilter: false),
            _ => throw new InvalidOperationException("This expression cannot be projected."),
        };
    }

    private static EvalValue EvaluateFilterValue(Expression expression, MatchRow match)
    {
        return expression switch
        {
            LiteralExpression literal => EvalValue.FromFilterLiteral(literal),
            PropertyExpression property => EvaluateProperty(property, match, forFilter: true),
            FunctionExpression function => EvaluateFunction(function, match, forFilter: true),
            _ => throw new GraphCypherParseException(
                expression.Start,
                expression.Length,
                "This value is not supported in WHERE."),
        };
    }

    private static EvalValue EvaluateFunction(
        FunctionExpression function,
        MatchRow match,
        bool forFilter)
    {
        string functionName = function.Name.ToUpperInvariant();
        EvalValue argument = EvalValue.Null;
        if (function.Arguments.Count > 0)
        {
            Expression argumentExpression = function.Arguments[0];
            argument = forFilter && argumentExpression is not VariableExpression
                ? EvaluateFilterValue(argumentExpression, match)
                : Evaluate(argumentExpression, match);
        }

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

    private static EvalValue EvaluateProperty(
        PropertyExpression property,
        MatchRow match,
        bool forFilter)
    {
        EvalValue owner = match.GetVariable(property.VariableName);
        if (owner.Node is BoundNode node)
        {
            return EvaluateNodeProperty(property.PropertyName, node, forFilter);
        }

        return owner.Relationship is BoundRelationship relationship
            ? EvaluateRelationshipProperty(property.PropertyName, relationship, forFilter)
            : EvalValue.Null;
    }

    private static EvalValue EvaluateNodeProperty(string propertyName, BoundNode node, bool forFilter)
    {
        return propertyName switch
        {
            "canonicalId" => EvalValue.FromText(node.Summary.Entity.CanonicalId),
            "displayLabel" => EvalValue.FromText(node.Summary.DisplayLabel),
            "typeName" => EvalValue.FromText(node.Summary.Entity.TypeName),
            "kind" when forFilter => EvalValue.FromText(
                ((int)node.Summary.Entity.Kind).ToString(CultureInfo.InvariantCulture)),
            "kind" => EvalValue.FromText(node.Summary.Entity.Kind.ToString()),
            "sourceNamespace" => EvalValue.FromText(node.Summary.Entity.SourceNamespace),
            "firstDiscoveredAt" => EvalValue.FromText(
                node.Summary.FirstDiscoveredAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            "lastUpdatedAt" => EvalValue.FromText(
                node.Summary.LastUpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            "degree" => EvalValue.FromNumber(node.Summary.Degree),
            _ => node.Properties.TryGetValue(propertyName, out string? propertyValue)
                ? EvalValue.FromText(propertyValue)
                : EvalValue.Null,
        };
    }

    private static EvalValue EvaluateRelationshipProperty(
        string propertyName,
        BoundRelationship relationship,
        bool forFilter)
    {
        _ = forFilter;
        return propertyName switch
        {
            "typeName" => EvalValue.FromText(relationship.Key.TypeName),
            "discriminator" => EvalValue.FromText(relationship.Key.Discriminator),
            "firstDiscoveredAt" => EvalValue.FromText(
                relationship.FirstDiscoveredAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            "lastUpdatedAt" => EvalValue.FromText(
                relationship.LastUpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
            _ => relationship.Properties.TryGetValue(propertyName, out string? propertyValue)
                ? EvalValue.FromText(propertyValue)
                : EvalValue.Null,
        };
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

    internal sealed class MatchRow
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
            if (binding.Kind == BindingKind.Node)
            {
                return EvalValue.FromNode(Nodes[binding.Index]);
            }

            return Relationship is null ? EvalValue.Null : EvalValue.FromRelationship(Relationship);
        }
    }

    internal sealed record BoundNode(
        GraphEntitySummary Summary,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyList<string> Labels,
        string PropertiesJson);

    internal sealed record BoundRelationship(
        GraphRelationshipKey Key,
        DateTimeOffset FirstDiscoveredAtUtc,
        DateTimeOffset LastUpdatedAtUtc,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyList<string> Labels,
        string PropertiesJson);

    private sealed record Projection(
        IReadOnlyList<GraphQueryColumn> Columns,
        IReadOnlyList<GraphQueryRow> Rows,
        IReadOnlyList<MatchRow> ViewportMatches,
        bool AreRowsTruncated);

    private sealed record ProjectedMatch(
        MatchRow Match,
        IReadOnlyList<EvalValue> Values,
        IReadOnlyList<EvalValue> OrderValues);

    internal sealed class EvalValue
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

        internal static EvalValue FromFilterLiteral(LiteralExpression literal)
        {
            return literal.Kind switch
            {
                LiteralKind.Null => Null,
                LiteralKind.Number => FromNumber(literal.NumberValue),
                LiteralKind.Boolean => FromText(literal.BooleanValue ? "true" : "false"),
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
}

#pragma warning restore SA1600, SA1602
