using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Kusto.Language;

namespace OpenKustoExplorer.Portable.Sessions;

/// <summary>
/// Extracts schema-bound identifier values from exact KQL predicates.
/// </summary>
public sealed class KustoPredicateInterestExtractor : IKustoPredicateInterestExtractor
{
    private const int MaximumStringLength = 4 * 1024;
    private const int MinimumStringLength = 3;

    /// <inheritdoc />
    public IReadOnlyList<KustoPredicateInterest> Extract(
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
        if (code.GetDiagnostics().Any())
        {
            return [];
        }

        List<KustoPredicateInterest> interests = [];

        code.Syntax.WalkNodes(node =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is FilterOperator filter)
            {
                ExtractFilterInterests(filter, interests, cancellationToken);
            }
        });

        KustoPredicateInterest[] distinct = interests
            .DistinctBy(interest => (
                interest.ColumnName.ToUpperInvariant(),
                interest.TypeName,
                interest.Value,
                interest.LiteralStart))
            .OrderBy(interest => interest.LiteralStart)
            .ToArray();
        return Array.AsReadOnly(distinct);
    }

    private static void ExtractFilterInterests(
        FilterOperator filter,
        ICollection<KustoPredicateInterest> interests,
        CancellationToken cancellationToken)
    {
        filter.Condition.WalkNodes(node =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasSafeBooleanAncestors(node, filter))
            {
                return;
            }

            if (node is BinaryExpression { Kind: SyntaxKind.EqualExpression } equality)
            {
                TryAddEquality(equality.Left, equality.Right, interests);
                TryAddEquality(equality.Right, equality.Left, interests);
            }
            else if (node is InExpression { Operator.Kind: SyntaxKind.InKeyword } inExpression
                && TryGetColumn(inExpression.Left, out ColumnSymbol? column))
            {
                for (int index = 0; index < inExpression.Right.Expressions.Count; index++)
                {
                    TryAddInterest(
                        column,
                        inExpression.Right.Expressions[index].Element,
                        interests);
                }
            }
        });
    }

    private static bool HasSafeBooleanAncestors(SyntaxNode node, FilterOperator filter)
    {
        SyntaxNode? ancestor = node.Parent;
        bool isSafe = true;

        while (ancestor is not null && ancestor != filter)
        {
            if (ancestor is Expression expression
                && expression is not ParenthesizedExpression
                && expression is not BinaryExpression { Kind: SyntaxKind.AndExpression })
            {
                isSafe = false;
                break;
            }

            ancestor = ancestor.Parent;
        }

        return isSafe;
    }

    private static void TryAddEquality(
        Expression possibleColumn,
        Expression possibleLiteral,
        ICollection<KustoPredicateInterest> interests)
    {
        if (TryGetColumn(possibleColumn, out ColumnSymbol? column))
        {
            TryAddInterest(column, possibleLiteral, interests);
        }
    }

    private static bool TryGetColumn(
        Expression expression,
        [NotNullWhen(true)] out ColumnSymbol? column)
    {
        column = expression is NameReference { ReferencedSymbol: ColumnSymbol boundColumn }
            ? boundColumn
            : null;
        return column is not null;
    }

    private static void TryAddInterest(
        ColumnSymbol column,
        Expression expression,
        ICollection<KustoPredicateInterest> interests)
    {
        if (expression is LiteralExpression literal
            && TryNormalize(column.Type.Name, literal.ConstantValue, out string? typeName, out string? value))
        {
            interests.Add(new KustoPredicateInterest(
                column.Name,
                typeName,
                value,
                literal.TextStart,
                literal.Width));
        }
    }

    private static bool TryNormalize(
        string sourceTypeName,
        object? constantValue,
        out string typeName,
        out string value)
    {
        typeName = sourceTypeName.ToLowerInvariant();
        value = string.Empty;

        if (constantValue is null)
        {
            return false;
        }

        if (typeName == ScalarTypes.String.Name)
        {
            value = Convert.ToString(constantValue, CultureInfo.InvariantCulture) ?? string.Empty;
            return value.Length is >= MinimumStringLength and <= MaximumStringLength
                && !string.IsNullOrWhiteSpace(value);
        }

        if (typeName == ScalarTypes.Guid.Name
            && Guid.TryParse(Convert.ToString(constantValue, CultureInfo.InvariantCulture), out Guid identifier))
        {
            value = identifier.ToString("D");
            return true;
        }

        if ((typeName == ScalarTypes.Int.Name || typeName == ScalarTypes.Long.Name)
            && long.TryParse(
                Convert.ToString(constantValue, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long integer)
            && integer is not -1 and not 0 and not 1)
        {
            value = integer.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }
}
