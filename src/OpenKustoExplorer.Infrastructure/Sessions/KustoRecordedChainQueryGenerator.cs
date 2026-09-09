using System.Text;
using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Infrastructure.Language;

namespace OpenKustoExplorer.Infrastructure.Sessions;

/// <summary>
/// Generates and validates KQL from a minimized strong-pivot relation plan.
/// </summary>
public sealed class KustoRecordedChainQueryGenerator : IKustoRecordedChainQueryGenerator
{
    private const int MaximumQueryLength = 256 * 1024;
    private const int MaximumStepCount = 32;

    /// <inheritdoc />
    public KustoGeneratedChainQuery Generate(
        KustoRelationalChainPlan plan,
        KustoDatabaseSchema databaseSchema)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(databaseSchema);
        if (!TargetMatches(plan, databaseSchema))
        {
            return Failure("The selected pivot chain does not target the active cluster and database.");
        }

        if (plan.Steps.Count > MaximumStepCount)
        {
            return Failure($"The pivot chain exceeds the {MaximumStepCount:N0}-step generation limit.");
        }

        string queryText;
        try
        {
            queryText = BuildQuery(plan, databaseSchema);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(exception.Message);
        }

        if (queryText.Length > MaximumQueryLength)
        {
            return Failure($"The generated query exceeds the {MaximumQueryLength:N0}-character limit.");
        }

        KustoCode code = KustoCode.ParseAndAnalyze(
            queryText,
            KustoGlobalStateFactory.Create(databaseSchema));
        string[] diagnostics = code.GetDiagnostics()
            .Select(diagnostic => diagnostic.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return diagnostics.Length == 0
            ? new KustoGeneratedChainQuery(queryText, [])
            : new KustoGeneratedChainQuery(string.Empty, diagnostics);
    }

    private static bool TargetMatches(KustoRelationalChainPlan plan, KustoDatabaseSchema schema)
    {
        return string.Equals(plan.ClusterUri.Host, schema.ClusterName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(plan.DatabaseName, schema.DatabaseName, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildQuery(
        KustoRelationalChainPlan plan,
        KustoDatabaseSchema databaseSchema)
    {
        KustoRelationalChainStep first = plan.Steps[0];
        StringBuilder builder = new();
        builder.Append("let chain_input = ")
            .Append(KustoKqlTextFormatter.FormatLiteral(first.Input))
            .AppendLine(";");
        for (int index = 0; index < plan.Steps.Count; index++)
        {
            KustoRelationalChainStep step = plan.Steps[index];
            string query = step.UsesQueryPipeline
                ? NormalizeStepQuery(step, databaseSchema, index == 0)
                : BuildSourceQuery(step, index == 0);
            builder.Append("let chain_step_")
                .Append(index + 1)
                .AppendLine(" = (");
            AppendIndented(builder, query);
            builder.AppendLine()
                .AppendLine(");");
        }

        builder.AppendLine("chain_step_1");

        for (int index = 1; index < plan.Steps.Count; index++)
        {
            KustoRelationalChainStep previous = plan.Steps[index - 1];
            KustoRelationalChainStep current = plan.Steps[index];
            builder.Append("| join kind=inner (chain_step_")
                .Append(index + 1)
                .Append(") on $left.")
                .Append(KustoKqlTextFormatter.FormatIdentifier(previous.OutputColumnName))
                .Append(" == $right.")
                .AppendLine(KustoKqlTextFormatter.FormatIdentifier(current.InputColumnName));
        }

        KustoRelationalChainStep last = plan.Steps[^1];
        builder.Append("| project ")
            .Append(KustoKqlTextFormatter.FormatIdentifier(first.InputColumnName))
            .Append(", ")
            .Append(KustoKqlTextFormatter.FormatIdentifier(last.OutputColumnName));
        return builder.ToString();
    }

    private static string BuildSourceQuery(KustoRelationalChainStep step, bool filterInput)
    {
        string sourceTable = KustoKqlTextFormatter.FormatIdentifier(step.SourceTableName);
        string sourceQuery = filterInput
            ? $"{sourceTable}{Environment.NewLine}| where {KustoKqlTextFormatter.FormatIdentifier(step.InputColumnName)} == chain_input"
            : sourceTable;
        return $"{sourceQuery}{Environment.NewLine}{CreateFinalProjection(step)}";
    }

    private static string NormalizeStepQuery(
        KustoRelationalChainStep step,
        KustoDatabaseSchema databaseSchema,
        bool parameterizeInput)
    {
        KustoCode code = KustoCode.ParseAndAnalyze(
            step.QueryText,
            KustoGlobalStateFactory.Create(databaseSchema));
        List<TextEdit> edits = [];
        IReadOnlyList<KustoPredicateInterest> predicates = new KustoPredicateInterestExtractor()
            .Extract(step.QueryText, databaseSchema);
        KustoPredicateInterest predicate = predicates.Single(interest => string.Equals(
            interest.ColumnName,
            step.InputColumnName,
            StringComparison.OrdinalIgnoreCase));
        if (parameterizeInput)
        {
            edits.Add(new TextEdit(predicate.LiteralStart, predicate.LiteralLength, "chain_input"));
        }
        else
        {
            BinaryExpression expression = FindExactPredicateExpression(code.Syntax, predicate);
            edits.Add(CreateConsumedPredicateEdit(step.QueryText, expression));
        }

        List<string> sourceDependencies = GetSourceDependencies(
            code.Syntax,
            step,
            databaseSchema);
        code.Syntax.WalkNodes(node =>
        {
            if (node is SummarizeOperator { Aggregates.Count: 0, ByClause: not null } summarize
                && summarize.Parent is PipeExpression pipe)
            {
                edits.Add(CreateSummaryEdit(step.QueryText, pipe, summarize));
            }
        });
        StringBuilder query = new(step.QueryText);
        foreach (TextEdit edit in edits.OrderByDescending(edit => edit.Start))
        {
            query.Remove(edit.Start, edit.Length);
            query.Insert(edit.Start, edit.Replacement);
        }

        string normalizedQuery = query.ToString().Trim();
        normalizedQuery = InsertDependencyProjection(normalizedQuery, sourceDependencies, databaseSchema);
        return $"{normalizedQuery}{Environment.NewLine}{CreateFinalProjection(step)}";
    }

    private static TextEdit CreateConsumedPredicateEdit(
        string queryText,
        BinaryExpression expression)
    {
        SyntaxNode? ancestor = expression.Parent;
        while (ancestor is not null && ancestor is not FilterOperator)
        {
            ancestor = ancestor.Parent;
        }

        if (ancestor is FilterOperator filter
            && filter.Parent is PipeExpression pipe
            && filter.Condition.TextStart == expression.TextStart
            && filter.Condition.End == expression.End)
        {
            int end = filter.End;
            while (end < queryText.Length && queryText[end] is ' ' or '\t')
            {
                end++;
            }

            if (end < queryText.Length && queryText[end] == '\r')
            {
                end++;
            }

            if (end < queryText.Length && queryText[end] == '\n')
            {
                end++;
            }

            return new TextEdit(
                pipe.Bar.TextStart,
                end - pipe.Bar.TextStart,
                string.Empty);
        }

        return new TextEdit(
            expression.TextStart,
            expression.End - expression.TextStart,
            "true");
    }

    private static List<string> GetSourceDependencies(
        SyntaxNode syntax,
        KustoRelationalChainStep step,
        KustoDatabaseSchema databaseSchema)
    {
        KustoTableSchema sourceTable = databaseSchema.Tables.FirstOrDefault(table => string.Equals(
            table.Name,
            step.SourceTableName,
            StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"The recorded source table '{step.SourceTableName}' is not available.");
        Dictionary<string, string> sourceColumns = sourceTable.Columns.ToDictionary(
            column => column.Name,
            column => column.Name,
            StringComparer.OrdinalIgnoreCase);
        PipeExpression pipeline = FindPipeline(syntax);
        List<PipeExpression> operations = GetPipelineOperations(pipeline);
        int firstShapeIndex = operations
            .Select((operation, index) => (operation, index))
            .Where(item => item.operation.Operator is not FilterOperator)
            .Select(item => item.index)
            .DefaultIfEmpty(operations.Count)
            .First();
        List<string> dependencies = [];
        HashSet<string> included = new(StringComparer.OrdinalIgnoreCase);
        foreach (QueryOperator queryOperator in operations
            .Skip(firstShapeIndex)
            .Select(operation => operation.Operator))
        {
            SyntaxNode dependencySyntax = queryOperator is JoinOperator join
                ? join.ConditionClause
                : queryOperator;
            AddReferencedSourceColumns(dependencySyntax, sourceColumns, dependencies, included);
        }

        bool outputIntroducedByJoin = operations.Any(operation =>
            operation.Operator is JoinOperator join
            && join.Expression.ResultType is TableSymbol rightTable
            && rightTable.Columns.Any(column => string.Equals(
                column.Name,
                step.OutputColumnName,
                StringComparison.OrdinalIgnoreCase)));
        if (!outputIntroducedByJoin
            && sourceColumns.TryGetValue(step.OutputColumnName, out string? outputColumn)
            && included.Add(outputColumn))
        {
            dependencies.Add(outputColumn);
        }

        if (sourceColumns.TryGetValue(step.InputColumnName, out string? inputColumn)
            && included.Add(inputColumn))
        {
            dependencies.Add(inputColumn);
        }

        return dependencies;
    }

    private static void AddReferencedSourceColumns(
        SyntaxNode syntax,
        Dictionary<string, string> sourceColumns,
        List<string> dependencies,
        HashSet<string> included)
    {
        syntax.WalkNodes(node =>
        {
            if (node is NameReference { ReferencedSymbol: ColumnSymbol column }
                && sourceColumns.TryGetValue(column.Name, out string? sourceColumn)
                && included.Add(sourceColumn))
            {
                dependencies.Add(sourceColumn);
            }
        });
    }

    private static string InsertDependencyProjection(
        string queryText,
        List<string> sourceDependencies,
        KustoDatabaseSchema databaseSchema)
    {
        if (sourceDependencies.Count == 0)
        {
            return queryText;
        }

        KustoCode code = KustoCode.ParseAndAnalyze(
            queryText,
            KustoGlobalStateFactory.Create(databaseSchema));
        PipeExpression pipeline = FindPipeline(code.Syntax);
        PipeExpression? firstShape = GetPipelineOperations(pipeline)
            .FirstOrDefault(operation => operation.Operator is not FilterOperator);
        if (firstShape is null)
        {
            return queryText;
        }

        int insertionStart = firstShape.Bar.TextStart;
        string indentation = GetLineIndentation(queryText, insertionStart);
        string projection = $"{CreateProjection(sourceDependencies)}{Environment.NewLine}{indentation}";
        return queryText.Insert(insertionStart, projection);
    }

    private static string GetLineIndentation(string text, int position)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        string indentation = text[lineStart..position];
        return string.IsNullOrWhiteSpace(indentation) ? indentation : string.Empty;
    }

    private static PipeExpression FindPipeline(SyntaxNode syntax)
    {
        PipeExpression? pipeline = null;
        syntax.WalkNodes(node =>
        {
            if (node is PipeExpression candidate
                && (pipeline is null || candidate.End > pipeline.End))
            {
                pipeline = candidate;
            }
        });
        return pipeline
            ?? throw new InvalidOperationException("The recorded conversion pipeline could not be located.");
    }

    private static List<PipeExpression> GetPipelineOperations(PipeExpression pipeline)
    {
        List<PipeExpression> operations = [];
        AddPipelineOperations(pipeline, operations);
        return operations;
    }

    private static void AddPipelineOperations(
        PipeExpression pipeline,
        List<PipeExpression> operations)
    {
        if (pipeline.Expression is PipeExpression previous)
        {
            AddPipelineOperations(previous, operations);
        }

        operations.Add(pipeline);
    }

    private static string CreateFinalProjection(KustoRelationalChainStep step)
    {
        return CreateProjection([step.InputColumnName, step.OutputColumnName]);
    }

    private static string CreateProjection(IEnumerable<string> columns)
    {
        return $"| project {string.Join(", ", columns
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(KustoKqlTextFormatter.FormatIdentifier))}";
    }

    private static BinaryExpression FindExactPredicateExpression(
        SyntaxNode syntax,
        KustoPredicateInterest predicate)
    {
        BinaryExpression? matchingExpression = null;
        syntax.WalkNodes(node =>
        {
            if (node is BinaryExpression { Kind: SyntaxKind.EqualExpression } expression
                && expression.TextStart <= predicate.LiteralStart
                && expression.End >= predicate.LiteralStart + predicate.LiteralLength)
            {
                matchingExpression = expression;
            }
        });
        return matchingExpression
            ?? throw new InvalidOperationException("The recorded conversion predicate could not be located.");
    }

    private static TextEdit CreateSummaryEdit(
        string queryText,
        PipeExpression pipe,
        SummarizeOperator summarize)
    {
        if (summarize.ResultType is not TableSymbol resultTable
            || resultTable.Columns.Count != summarize.ByClause.Expressions.Count)
        {
            throw new InvalidOperationException("The recorded conversion summary has unsupported output columns.");
        }

        List<string> assignments = [];
        for (int index = 0; index < summarize.ByClause.Expressions.Count; index++)
        {
            Expression expression = summarize.ByClause.Expressions[index].Element;
            string expressionText = queryText[expression.TextStart..expression.End];
            string outputName = resultTable.Columns[index].Name;
            if (expression is NameReference nameReference
                && string.Equals(
                    nameReference.ReferencedSymbol?.Name,
                    outputName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            assignments.Add(expression is SimpleNamedExpression
                ? expressionText
                : $"{KustoKqlTextFormatter.FormatIdentifier(outputName)} = {expressionText}");
        }

        string replacement = assignments.Count == 0
            ? string.Empty
            : $"| extend {string.Join(", ", assignments)}";
        return new TextEdit(
            pipe.Bar.TextStart,
            summarize.End - pipe.Bar.TextStart,
            replacement);
    }

    private static void AppendIndented(StringBuilder builder, string value)
    {
        string[] lines = value.ReplaceLineEndings("\n").Split('\n');
        foreach (string line in lines)
        {
            builder.Append("    ").AppendLine(line);
        }

        builder.Length -= Environment.NewLine.Length;
    }

    private static KustoGeneratedChainQuery Failure(string message)
    {
        return new KustoGeneratedChainQuery(string.Empty, [message]);
    }

    private sealed record TextEdit(int Start, int Length, string Replacement);
}
