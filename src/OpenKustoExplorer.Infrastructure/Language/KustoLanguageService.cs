using System.Text;
using Kusto.Language;
using Kusto.Language.Editor;
using Kusto.Language.Syntax;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Infrastructure.Language;

/// <summary>
/// Provides schema-aware KQL editor intelligence using the official Kusto language service.
/// </summary>
/// <remarks>
/// Instances are stateless and safe to use concurrently. Each analysis operates on an immutable
/// document and schema snapshot, and cancellation is forwarded to the Kusto parser and binder.
/// </remarks>
public sealed class KustoLanguageService : IKustoLanguageService
{
    /// <inheritdoc />
    public KustoGraphQueryPlan? GetGraphQueryPlanAtPosition(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caretPosition);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caretPosition, text.Length);

        KustoQuerySelection? selection = GetQueryAtPosition(text, caretPosition);
        KustoGraphQueryPlan? plan = null;

        if (selection is not null)
        {
            KustoCode code = KustoCode.Parse(selection.Text);
            ExpressionStatement? finalStatement = FindFinalExpressionStatement(code.Syntax);
            MakeGraphOperator? namedMakeGraph = finalStatement?.Expression is NameReference graphReference
                ? FindNamedMakeGraphOperator(code.Syntax, graphReference)
                : null;

            if (finalStatement?.Expression is PipeExpression { Operator: MakeGraphOperator makeGraph })
            {
                plan = CreateGraphQueryPlan(
                    selection,
                    finalStatement.Expression,
                    KustoGraphSourceKind.MakeGraph,
                    makeGraph.SourceColumn.SimpleName,
                    makeGraph.TargetColumn.SimpleName);
            }
            else if (finalStatement?.Expression is NameReference && namedMakeGraph is not null)
            {
                plan = CreateGraphQueryPlan(
                    selection,
                    finalStatement.Expression,
                    KustoGraphSourceKind.MakeGraph,
                    namedMakeGraph.SourceColumn.SimpleName,
                    namedMakeGraph.TargetColumn.SimpleName);
            }
            else if (finalStatement?.Expression is FunctionCallExpression graphFunction
                && string.Equals(graphFunction.Name.SimpleName, "graph", StringComparison.OrdinalIgnoreCase))
            {
                plan = CreateGraphQueryPlan(
                    selection,
                    finalStatement.Expression,
                    KustoGraphSourceKind.GraphFunction,
                    null,
                    null);
            }
        }

        return plan;
    }

    /// <inheritdoc />
    public KustoQuerySelection? GetQueryAtPosition(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caretPosition);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caretPosition, text.Length);

        CodeScript script = CodeScript.From(text, GlobalState.Default);
        CodeBlock? block = GetNearestExecutableBlock(script, caretPosition);
        KustoQuerySelection? selection = block is null ? null : CreateQuerySelection(text, block);

        return selection;
    }

    /// <inheritdoc />
    public KustoVisualization? GetVisualizationAtPosition(string text, int caretPosition)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caretPosition);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caretPosition, text.Length);

        CodeScript script = CodeScript.From(text, GlobalState.Default);
        CodeBlock? block = GetNearestExecutableBlock(script, caretPosition);
        KustoVisualization? visualization = null;

        if (block is not null)
        {
            RenderOperator? renderOperator = null;
            KustoCode.Parse(block.Text).Syntax.WalkNodes(node =>
            {
                if (node is RenderOperator candidate)
                {
                    renderOperator = candidate;
                }
            });

            if (renderOperator is not null
                && KustoVisualization.TryParseKind(renderOperator.ChartType.ValueText, out KustoVisualizationKind kind))
            {
                visualization = new KustoVisualization(kind);
            }
        }

        return visualization;
    }

    /// <inheritdoc />
    public KustoLanguageAnalysis Analyze(
        string text,
        int caretPosition,
        KustoDatabaseSchema databaseSchema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(databaseSchema);
        ArgumentOutOfRangeException.ThrowIfNegative(caretPosition);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caretPosition, text.Length);
        cancellationToken.ThrowIfCancellationRequested();

        GlobalState globalState = KustoGlobalStateFactory.Create(databaseSchema);
        CodeScript script = CodeScript.From(text, globalState);
        List<KustoClassification> classifications = [];
        List<KustoDiagnostic> diagnostics = [];

        foreach (CodeBlock block in script.Blocks.Where(IsExecutableBlock))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClassificationInfo classificationInfo = block.Service.GetClassifications(
                block.Start,
                block.Length,
                cancellationToken: cancellationToken);
            classifications.AddRange(classificationInfo.Classifications.Select(
                CreateClassification));
            diagnostics.AddRange(block.Service
                .GetDiagnostics(cancellationToken: cancellationToken)
                .Select(CreateDiagnostic));
        }

        CodeBlock? completionBlock = GetNearestBlock(script, caretPosition);
        CompletionInfo completionInfo = completionBlock is null
            ? CompletionInfo.Empty
            : completionBlock.Service.GetCompletionItems(
                Math.Clamp(caretPosition, completionBlock.Start, completionBlock.End),
                cancellationToken: cancellationToken);
        int completionEditStart = completionBlock is null ? caretPosition : completionInfo.EditStart;
        bool isQuerySourceStart = completionBlock is not null
            && IsQuerySourceStart(completionBlock, caretPosition);
        CompletionItem[] completionItems = completionInfo.Items
            .Where(item => !isQuerySourceStart || IsValidAtQuerySourceStart(item.Kind))
            .ToArray();
        IReadOnlyList<KustoCompletion> completions = KustoCompletionRanker.Rank(
            completionItems,
            text,
            caretPosition,
            completionEditStart,
            isQuerySourceStart,
            classifications);

        KustoLanguageAnalysis analysis = new(
            classifications,
            completions,
            diagnostics,
            completionEditStart,
            completionInfo.EditLength);

        return analysis;
    }

    private static KustoGraphQueryPlan? CreateGraphQueryPlan(
        KustoQuerySelection selection,
        Expression graphExpression,
        KustoGraphSourceKind sourceKind,
        string? sourceColumnName,
        string? targetColumnName)
    {
        string identifierPrefix = CreateUniqueGraphIdentifierPrefix(selection.Text);
        string nodeTableName = $"{identifierPrefix}_nodes";
        string edgeTableName = $"{identifierPrefix}_edges";
        string nodeHashColumnName = $"{identifierPrefix}_node_hash";
        string sourceHashColumnName = $"{identifierPrefix}_source_hash";
        string targetHashColumnName = $"{identifierPrefix}_target_hash";
        string graphExpressionText = selection.Text.Substring(graphExpression.TextStart, graphExpression.Width).Trim();
        StringBuilder exportQuery = new();
        exportQuery.Append(selection.Text.AsSpan(0, graphExpression.TextStart));
        exportQuery.AppendLine(graphExpressionText);
        exportQuery.Append("| graph-to-table nodes as ")
            .Append(nodeTableName)
            .Append(" with_node_id=")
            .Append(nodeHashColumnName)
            .Append(", edges as ")
            .Append(edgeTableName)
            .Append(" with_source_id=")
            .Append(sourceHashColumnName)
            .Append(" with_target_id=")
            .Append(targetHashColumnName)
            .AppendLine(";");
        exportQuery.Append(nodeTableName).AppendLine(";");
        exportQuery.Append(edgeTableName);
        string exportQueryText = exportQuery.ToString();
        KustoCode exportedCode = KustoCode.Parse(exportQueryText);
        bool hasGraphExport = false;
        exportedCode.Syntax.WalkNodes(node => hasGraphExport |= node is GraphToTableOperator);
        KustoGraphQueryPlan? plan = null;

        if (!exportedCode.Syntax.HasSyntaxDiagnostics && hasGraphExport)
        {
            plan = new KustoGraphQueryPlan(
                selection,
                sourceKind,
                exportQueryText,
                nodeTableName,
                edgeTableName,
                nodeHashColumnName,
                sourceHashColumnName,
                targetHashColumnName,
                sourceColumnName,
                targetColumnName);
        }

        return plan;
    }

    private static string CreateUniqueGraphIdentifierPrefix(string queryText)
    {
        const string BasePrefix = "__oke_graph_export";
        string prefix = BasePrefix;
        int suffix = 1;

        while (queryText.Contains(prefix, StringComparison.Ordinal))
        {
            prefix = $"{BasePrefix}_{suffix}";
            suffix++;
        }

        return prefix;
    }

    private static MakeGraphOperator? FindNamedMakeGraphOperator(
        SyntaxNode syntax,
        NameReference graphReference)
    {
        MakeGraphOperator? makeGraph = null;
        int declarationStart = -1;
        syntax.WalkNodes(node =>
        {
            if (node is LetStatement declaration
                && declaration.Expression is PipeExpression { Operator: MakeGraphOperator candidateMakeGraph }
                && declaration.TextStart < graphReference.TextStart
                && declaration.TextStart > declarationStart
                && string.Equals(
                    declaration.Name.SimpleName,
                    graphReference.SimpleName,
                    StringComparison.OrdinalIgnoreCase))
            {
                makeGraph = candidateMakeGraph;
                declarationStart = declaration.TextStart;
            }
        });

        return makeGraph;
    }

    private static ExpressionStatement? FindFinalExpressionStatement(SyntaxNode syntax)
    {
        ExpressionStatement? finalStatement = null;
        syntax.WalkNodes(node =>
        {
            if (node is ExpressionStatement candidate
                && (finalStatement is null || candidate.TextStart > finalStatement.TextStart))
            {
                finalStatement = candidate;
            }
        });
        return finalStatement;
    }

    private static KustoQuerySelection CreateQuerySelection(string text, CodeBlock block)
    {
        int start = block.Start;
        int end = block.End;

        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        string selectedText = text.Substring(start, end - start);
        return new KustoQuerySelection(selectedText, start, end - start);
    }

    private static int GetBlockDistance(CodeBlock block, int caretPosition)
    {
        int distance = caretPosition < block.Start
            ? block.Start - caretPosition
            : Math.Max(0, caretPosition - block.End);

        return distance;
    }

    private static CodeBlock? GetNearestExecutableBlock(CodeScript script, int caretPosition)
    {
        CodeBlock? block = script.Blocks
            .Where(IsExecutableBlock)
            .OrderBy(candidate => GetBlockDistance(candidate, caretPosition))
            .ThenBy(candidate => candidate.Start > caretPosition)
            .FirstOrDefault();

        return block;
    }

    private static CodeBlock? GetNearestBlock(CodeScript script, int caretPosition)
    {
        CodeBlock? block = script.Blocks
            .OrderBy(candidate => GetBlockDistance(candidate, caretPosition))
            .ThenBy(candidate => candidate.Start > caretPosition)
            .FirstOrDefault();

        return block;
    }

    private static bool IsExecutableBlock(CodeBlock block)
    {
        return !string.IsNullOrWhiteSpace(block.Text);
    }

    private static bool IsQuerySourceStart(CodeBlock block, int caretPosition)
    {
        int localPosition = Math.Clamp(caretPosition - block.Start, 0, block.Length);
        int tokenStart = localPosition;

        while (tokenStart > 0
            && (char.IsLetterOrDigit(block.Text[tokenStart - 1]) || block.Text[tokenStart - 1] == '_'))
        {
            tokenStart--;
        }

        int previousPosition = tokenStart - 1;

        while (previousPosition >= 0 && char.IsWhiteSpace(block.Text[previousPosition]))
        {
            previousPosition--;
        }

        return previousPosition < 0 || block.Text[previousPosition] == ';';
    }

    private static bool IsValidAtQuerySourceStart(CompletionKind kind)
    {
        return kind is not CompletionKind.CommandPrefix
            and not CompletionKind.TabularSuffix
            and not CompletionKind.ScalarInfix
            and not CompletionKind.Column
            and not CompletionKind.Parameter
            and not CompletionKind.Punctuation;
    }

    private static KustoClassification CreateClassification(ClassifiedRange classifiedRange)
    {
        KustoClassification classification = new(
            classifiedRange.Kind.ToString(),
            classifiedRange.Start,
            classifiedRange.Length);

        return classification;
    }

    private static KustoDiagnostic CreateDiagnostic(Diagnostic diagnostic)
    {
        KustoDiagnostic result = new(
            diagnostic.Code,
            diagnostic.Severity,
            diagnostic.Message,
            diagnostic.Start,
            diagnostic.Length);

        return result;
    }
}
