using Lucide.Avalonia;
using OpenKustoExplorer.Desktop.Editor;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies completion popup category presentation.
/// </summary>
public sealed class KustoCompletionItemContentTests
{
    /// <summary>
    /// Verifies each completion kind uses its concise label and semantic style.
    /// </summary>
    /// <param name="kind">The completion kind.</param>
    /// <param name="expectedIcon">The expected category icon.</param>
    /// <param name="expectedLabel">The expected category label.</param>
    /// <param name="expectedStyleClass">The expected semantic style class.</param>
    [Theory]
    [InlineData("Table", LucideIconKind.Table2, "table", "completionEntity")]
    [InlineData("MaterialiedView", LucideIconKind.TableProperties, "materialized view", "completionEntity")]
    [InlineData("StoredQueryResult", LucideIconKind.Table2, "stored result", "completionEntity")]
    [InlineData("Column", LucideIconKind.Columns3, "column", "completionName")]
    [InlineData("Variable", LucideIconKind.Variable, "variable", "completionVariable")]
    [InlineData("Parameter", LucideIconKind.Variable, "parameter", "completionVariable")]
    [InlineData("LocalFunction", LucideIconKind.SquareFunction, "local function", "completionFunction")]
    [InlineData("DatabaseFunction", LucideIconKind.SquareFunction, "database function", "completionFunction")]
    [InlineData("BuiltInFunction", LucideIconKind.SquareFunction, "function", "completionFunction")]
    [InlineData("AggregateFunction", LucideIconKind.Sigma, "aggregate", "completionFunction")]
    [InlineData("TabularPrefix", LucideIconKind.Workflow, "operator", "completionOperator")]
    [InlineData("TabularSuffix", LucideIconKind.Workflow, "operator", "completionOperator")]
    [InlineData("QueryPrefix", LucideIconKind.Workflow, "operator", "completionOperator")]
    [InlineData("CommandPrefix", LucideIconKind.Workflow, "operator", "completionOperator")]
    [InlineData("ScalarInfix", LucideIconKind.Code, "operator", "completionOperator")]
    [InlineData("Keyword", LucideIconKind.KeyRound, "keyword", "completionKeyword")]
    [InlineData("Punctuation", LucideIconKind.Parentheses, "syntax", "completionSyntax")]
    [InlineData("Syntax", LucideIconKind.Parentheses, "syntax", "completionSyntax")]
    [InlineData("ScalarType", LucideIconKind.Braces, "type", "completionSyntax")]
    [InlineData("Database", LucideIconKind.Database, "database", "completionEntity")]
    [InlineData("Cluster", LucideIconKind.Database, "cluster", "completionEntity")]
    [InlineData("RenderChart", LucideIconKind.ChartNoAxesCombined, "chart", "completionOperator")]
    [InlineData("Graph", LucideIconKind.Workflow, "graph", "completionEntity")]
    [InlineData("FutureKind", LucideIconKind.Code, "futurekind", "completionSyntax")]
    public void CompletionKindBuildsExpectedCategory(
        string kind,
        LucideIconKind expectedIcon,
        string expectedLabel,
        string expectedStyleClass)
    {
        KustoCompletionItemContent.CompletionCategory category =
            KustoCompletionItemContent.GetCategory(kind);

        Assert.Equal(expectedIcon, category.Icon);
        Assert.Equal(expectedLabel, category.Label);
        Assert.Equal(expectedStyleClass, category.StyleClass);
    }

    /// <summary>
    /// Verifies completion content rejects a missing completion.
    /// </summary>
    [Fact]
    public void ConstructorRejectsNullCompletion()
    {
        Assert.Throws<ArgumentNullException>(() => new KustoCompletionItemContent(null!));
    }
}
