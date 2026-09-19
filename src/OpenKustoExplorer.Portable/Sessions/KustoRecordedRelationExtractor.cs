using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Kusto.Language;

namespace OpenKustoExplorer.Portable.Sessions;

/// <summary>
/// Extracts direct table-column lineage for conservative generated-query planning.
/// </summary>
public sealed class KustoRecordedRelationExtractor : IKustoRecordedRelationExtractor
{
    /// <inheritdoc />
    public KustoRecordedRelationDescriptor? Extract(
        string queryText,
        KustoDatabaseSchema databaseSchema,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentNullException.ThrowIfNull(databaseSchema);
        cancellationToken.ThrowIfCancellationRequested();
        KustoCode code = KustoCode.ParseAndAnalyze(
            queryText,
            KustoGlobalStateFactory.Create(databaseSchema),
            cancellationToken);
        IReadOnlyList<KustoPredicateInterest> predicateInterests = new KustoPredicateInterestExtractor()
            .Extract(queryText, databaseSchema, cancellationToken);

        if (code.Syntax is not QueryBlock { Statements.Count: 1 } queryBlock
            || queryBlock.Statements[0].Element is not ExpressionStatement statement
            || statement.Expression is not PipeExpression pipeExpression
            || !TryGetRootTable(pipeExpression, out TableSymbol table)
            || predicateInterests.Count != 1
            || !HasExactPredicateExpression(pipeExpression, predicateInterests[0])
            || !IsComposablePipeline(pipeExpression)
            || code.GetDiagnostics().Any())
        {
            return null;
        }

        if (pipeExpression.ResultType is not TableSymbol resultTable)
        {
            return null;
        }

        KustoSourceColumnLineage[] columns = resultTable.Columns
            .Select(column => new KustoSourceColumnLineage(column.Name, column.Name))
            .ToArray();
        return new KustoRecordedRelationDescriptor(table.Name, true, columns);
    }

    private static bool HasExactPredicateExpression(
        SyntaxNode syntax,
        KustoPredicateInterest predicate)
    {
        bool found = false;
        syntax.WalkNodes(node =>
        {
            if (node is BinaryExpression { Kind: SyntaxKind.EqualExpression } expression
                && IsColumnLiteralEquality(expression)
                && expression.TextStart <= predicate.LiteralStart
                && expression.End >= predicate.LiteralStart + predicate.LiteralLength)
            {
                found = true;
            }
        });
        return found;
    }

    private static bool IsColumnLiteralEquality(BinaryExpression expression)
    {
        bool columnOnLeft = expression.Left is NameReference { ReferencedSymbol: ColumnSymbol }
            && expression.Right is LiteralExpression;
        bool columnOnRight = expression.Right is NameReference { ReferencedSymbol: ColumnSymbol }
            && expression.Left is LiteralExpression;
        return columnOnLeft || columnOnRight;
    }

    private static bool IsComposablePipeline(Expression expression)
    {
        if (expression is NameReference { ReferencedSymbol: TableSymbol })
        {
            return true;
        }

        if (expression is not PipeExpression pipeExpression
            || !IsComposablePipeline(pipeExpression.Expression))
        {
            return false;
        }

        return pipeExpression.Operator switch
        {
            FilterOperator => true,
            MvExpandOperator => true,
            ExtendOperator => true,
            SummarizeOperator summarize => summarize.Aggregates.Count == 0
                && summarize.ByClause is not null,
            JoinOperator join => IsComposablePipeline(join.Expression),
            _ => false,
        };
    }

    private static bool TryGetRootTable(Expression expression, out TableSymbol table)
    {
        while (expression is PipeExpression pipeExpression)
        {
            expression = pipeExpression.Expression;
        }

        if ((expression as NameReference)?.ReferencedSymbol is TableSymbol rootTable)
        {
            table = rootTable;
            return true;
        }

        table = null!;
        return false;
    }
}
