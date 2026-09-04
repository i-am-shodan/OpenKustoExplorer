using System.Globalization;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Applies tab-specific alternating and conditional formatting to materialized result rows.
/// </summary>
public static class KustoResultFormattingEngine
{
    private const string AlternatingRowColor = "#126B7280";
    private const string TransparentColor = "#00000000";

    /// <summary>
    /// Applies all active formatting settings to result rows.
    /// </summary>
    /// <param name="columns">The result columns in server order.</param>
    /// <param name="rows">The materialized result rows.</param>
    /// <param name="useAlternatingRows">Whether alternating rows are shaded.</param>
    /// <param name="rules">The conditional-formatting rules.</param>
    public static void Apply(
        IReadOnlyList<KustoResultColumn> columns,
        IReadOnlyList<KustoResultRowViewModel> rows,
        bool useAlternatingRows,
        IReadOnlyList<KustoConditionalFormatRuleViewModel> rules)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(rules);

        Reset(rows, useAlternatingRows);

        foreach (KustoConditionalFormatRuleViewModel rule in rules)
        {
            int columnIndex = FindColumnIndex(columns, rule.ColumnName);
            if (columnIndex >= 0)
            {
                double? percentileThreshold = GetPercentileThreshold(rows, columnIndex, rule);
                ApplyRule(rows, columnIndex, rule, percentileThreshold);
            }
        }
    }

    private static void Reset(IReadOnlyList<KustoResultRowViewModel> rows, bool useAlternatingRows)
    {
        foreach (KustoResultRowViewModel row in rows)
        {
            string rowColor = useAlternatingRows && row.RowIndex % 2 == 1
                ? AlternatingRowColor
                : TransparentColor;
            row.SetBackground(rowColor);

            foreach (KustoResultCellViewModel cell in row.Cells)
            {
                cell.SetBackground(TransparentColor);
            }
        }
    }

    private static int FindColumnIndex(IReadOnlyList<KustoResultColumn> columns, string columnName)
    {
        int matchingIndex = -1;

        for (int index = 0; index < columns.Count && matchingIndex < 0; index++)
        {
            if (string.Equals(columns[index].Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                matchingIndex = index;
            }
        }

        return matchingIndex;
    }

    private static void ApplyRule(
        IReadOnlyList<KustoResultRowViewModel> rows,
        int columnIndex,
        KustoConditionalFormatRuleViewModel rule,
        double? percentileThreshold)
    {
        foreach (KustoResultRowViewModel row in rows)
        {
            KustoResultCellViewModel cell = row.Cells[columnIndex];
            if (Matches(cell.Text, rule, percentileThreshold))
            {
                if (rule.Target == KustoConditionalFormatTarget.Row)
                {
                    row.SetBackground(rule.ColorHex);
                }
                else
                {
                    cell.SetBackground(rule.ColorHex);
                }
            }
        }
    }

    private static bool Matches(
        string text,
        KustoConditionalFormatRuleViewModel rule,
        double? percentileThreshold)
    {
        bool matches = rule.Comparison switch
        {
            KustoConditionalFormatOperator.Equals => ValuesEqual(text, rule.ComparisonValue),
            KustoConditionalFormatOperator.GreaterThan => CompareNumber(text, rule.ComparisonValue, comparison => comparison > 0),
            KustoConditionalFormatOperator.GreaterThanOrEqual => CompareNumber(text, rule.ComparisonValue, comparison => comparison >= 0),
            KustoConditionalFormatOperator.LessThan => CompareNumber(text, rule.ComparisonValue, comparison => comparison < 0),
            KustoConditionalFormatOperator.LessThanOrEqual => CompareNumber(text, rule.ComparisonValue, comparison => comparison <= 0),
            KustoConditionalFormatOperator.TopPercent => MatchesPercentile(text, percentileThreshold, top: true),
            KustoConditionalFormatOperator.BottomPercent => MatchesPercentile(text, percentileThreshold, top: false),
            KustoConditionalFormatOperator.TextMatches => string.Equals(text, rule.ComparisonValue, StringComparison.OrdinalIgnoreCase),
            KustoConditionalFormatOperator.TextStartsWith => text.StartsWith(rule.ComparisonValue, StringComparison.OrdinalIgnoreCase),
            KustoConditionalFormatOperator.TextEndsWith => text.EndsWith(rule.ComparisonValue, StringComparison.OrdinalIgnoreCase),
            KustoConditionalFormatOperator.TextContains => text.Contains(rule.ComparisonValue, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

        return matches;
    }

    private static bool ValuesEqual(string left, string right)
    {
        double leftValue = 0;
        double rightValue = 0;
        bool bothNumeric = TryParseNumber(left, out leftValue)
            && TryParseNumber(right, out rightValue);
        return bothNumeric
            ? Math.Abs(leftValue - rightValue) < double.Epsilon
            : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompareNumber(string left, string right, Func<int, bool> predicate)
    {
        double leftValue = 0;
        double rightValue = 0;
        bool comparable = TryParseNumber(left, out leftValue)
            && TryParseNumber(right, out rightValue);
        return comparable && predicate(leftValue.CompareTo(rightValue));
    }

    private static bool MatchesPercentile(string text, double? threshold, bool top)
    {
        double value = 0;
        bool matches = threshold is not null && TryParseNumber(text, out value);
        return matches && (top ? value >= threshold : value <= threshold);
    }

    private static double? GetPercentileThreshold(
        IReadOnlyList<KustoResultRowViewModel> rows,
        int columnIndex,
        KustoConditionalFormatRuleViewModel rule)
    {
        bool isPercentile = rule.Comparison is KustoConditionalFormatOperator.TopPercent
            or KustoConditionalFormatOperator.BottomPercent;
        double? threshold = null;

        if (isPercentile
            && TryParseNumber(rule.ComparisonValue, out double percentage)
            && percentage > 0)
        {
            double[] values = rows
                .Select(row => row.Cells[columnIndex].Text)
                .Where(text => TryParseNumber(text, out _))
                .Select(text => double.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture))
                .OrderBy(value => value)
                .ToArray();

            if (values.Length > 0)
            {
                double boundedPercentage = Math.Min(100, percentage);
                int count = Math.Max(1, (int)Math.Ceiling(values.Length * boundedPercentage / 100));
                threshold = rule.Comparison == KustoConditionalFormatOperator.TopPercent
                    ? values[^count]
                    : values[count - 1];
            }
        }

        return threshold;
    }

    private static bool TryParseNumber(string text, out double value)
    {
        return double.TryParse(
            text,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value)
            && double.IsFinite(value);
    }
}
