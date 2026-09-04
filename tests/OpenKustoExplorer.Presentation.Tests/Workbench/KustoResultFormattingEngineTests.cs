using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Workbench;

/// <summary>
/// Verifies alternating and conditional formatting behavior.
/// </summary>
public sealed class KustoResultFormattingEngineTests
{
    /// <summary>
    /// Verifies text rules can color a complete row while preserving alternating backgrounds elsewhere.
    /// </summary>
    [Fact]
    public void ApplyColorsMatchingRowAndAlternatingRows()
    {
        KustoResultColumn[] columns =
        [
            new KustoResultColumn("State", "string"),
            new KustoResultColumn("Events", "long"),
        ];
        KustoResultRowViewModel[] rows = CreateRows(columns);
        KustoConditionalFormatRuleViewModel rule = CreateRule(
            "State",
            KustoConditionalFormatOperator.TextContains,
            "tex",
            KustoConditionalFormatTarget.Row,
            "#FECACA");

        KustoResultFormattingEngine.Apply(columns, rows, true, [rule]);

        Assert.Equal("#FECACA", rows[0].BackgroundHex);
        Assert.Equal("#126B7280", rows[1].BackgroundHex);
        Assert.Equal("#00000000", rows[0].Cells[0].BackgroundHex);
    }

    /// <summary>
    /// Verifies numeric and top-percent rules color only matching cells.
    /// </summary>
    [Fact]
    public void ApplySupportsNumericAndPercentileCellRules()
    {
        KustoResultColumn[] columns =
        [
            new KustoResultColumn("State", "string"),
            new KustoResultColumn("Events", "long"),
        ];
        KustoResultRowViewModel[] rows = CreateRows(columns);
        KustoConditionalFormatRuleViewModel greaterThan = CreateRule(
            "Events",
            KustoConditionalFormatOperator.GreaterThan,
            "15",
            KustoConditionalFormatTarget.Cell,
            "#BFDBFE");
        KustoConditionalFormatRuleViewModel topHalf = CreateRule(
            "Events",
            KustoConditionalFormatOperator.TopPercent,
            "50",
            KustoConditionalFormatTarget.Cell,
            "#BBF7D0");

        KustoResultFormattingEngine.Apply(columns, rows, false, [greaterThan, topHalf]);

        Assert.Equal("#00000000", rows[0].Cells[1].BackgroundHex);
        Assert.Equal("#BBF7D0", rows[1].Cells[1].BackgroundHex);
    }

    /// <summary>
    /// Verifies all exact, ordered numeric, and text comparison operators.
    /// </summary>
    /// <param name="comparison">The comparison operator.</param>
    /// <param name="comparisonValue">The comparison value.</param>
    /// <param name="expectedRowIndex">The row expected to match.</param>
    [Theory]
    [InlineData(KustoConditionalFormatOperator.Equals, "20", 1)]
    [InlineData(KustoConditionalFormatOperator.GreaterThanOrEqual, "20", 1)]
    [InlineData(KustoConditionalFormatOperator.LessThan, "15", 0)]
    [InlineData(KustoConditionalFormatOperator.LessThanOrEqual, "10", 0)]
    [InlineData(KustoConditionalFormatOperator.TextMatches, "texas", 0)]
    [InlineData(KustoConditionalFormatOperator.TextStartsWith, "Tex", 0)]
    [InlineData(KustoConditionalFormatOperator.TextEndsWith, "hio", 1)]
    [InlineData(KustoConditionalFormatOperator.TextContains, "xas", 0)]
    public void ApplySupportsComparisonOperator(
        KustoConditionalFormatOperator comparison,
        string comparisonValue,
        int expectedRowIndex)
    {
        KustoResultColumn[] columns =
        [
            new KustoResultColumn("State", "string"),
            new KustoResultColumn("Events", "long"),
        ];
        KustoResultRowViewModel[] rows = CreateRows(columns);
        string columnName = comparison >= KustoConditionalFormatOperator.TextMatches ? "State" : "Events";
        KustoConditionalFormatRuleViewModel rule = CreateRule(
            columnName,
            comparison,
            comparisonValue,
            KustoConditionalFormatTarget.Cell,
            "#FEF08A");

        KustoResultFormattingEngine.Apply(columns, rows, false, [rule]);

        int columnIndex = columnName == "State" ? 0 : 1;
        Assert.Equal("#FEF08A", rows[expectedRowIndex].Cells[columnIndex].BackgroundHex);
    }

    /// <summary>
    /// Verifies bottom-percent rules select the lowest values.
    /// </summary>
    [Fact]
    public void ApplySupportsBottomPercentRule()
    {
        KustoResultColumn[] columns =
        [
            new KustoResultColumn("State", "string"),
            new KustoResultColumn("Events", "long"),
        ];
        KustoResultRowViewModel[] rows = CreateRows(columns);
        KustoConditionalFormatRuleViewModel rule = CreateRule(
            "Events",
            KustoConditionalFormatOperator.BottomPercent,
            "50",
            KustoConditionalFormatTarget.Cell,
            "#DDD6FE");

        KustoResultFormattingEngine.Apply(columns, rows, false, [rule]);

        Assert.Equal("#DDD6FE", rows[0].Cells[1].BackgroundHex);
        Assert.Equal("#00000000", rows[1].Cells[1].BackgroundHex);
    }

    private static KustoResultRowViewModel[] CreateRows(IReadOnlyList<KustoResultColumn> columns)
    {
        return
        [
            new KustoResultRowViewModel(new KustoResultRow(["Texas", "10"]), 0, columns),
            new KustoResultRowViewModel(new KustoResultRow(["Ohio", "20"]), 1, columns),
        ];
    }

    private static KustoConditionalFormatRuleViewModel CreateRule(
        string columnName,
        KustoConditionalFormatOperator comparison,
        string value,
        KustoConditionalFormatTarget target,
        string color)
    {
        KustoConditionalFormatRule rule = new(Guid.NewGuid(), columnName, comparison, value, target, color);
        return new KustoConditionalFormatRuleViewModel(rule, _ => { });
    }
}
