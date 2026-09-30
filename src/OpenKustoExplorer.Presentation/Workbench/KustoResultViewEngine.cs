using System.Globalization;
using OpenKustoExplorer.Application.Diagnostics;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Derives visible materialized rows from tab-specific search, filters, and sorting.
/// </summary>
internal static class KustoResultViewEngine
{
    /// <summary>
    /// Applies all active local result transforms without mutating source row order.
    /// </summary>
    /// <param name="sourceRows">The complete materialized row set in server order.</param>
    /// <param name="columns">The result columns and their active view state.</param>
    /// <param name="searchText">Text matched against every cell in a row.</param>
    /// <returns>The visible rows in active sort order.</returns>
    internal static IReadOnlyList<KustoResultRowViewModel> Apply(
        IReadOnlyList<KustoResultRowViewModel> sourceRows,
        IReadOnlyList<KustoResultColumnViewModel> columns,
        string searchText)
    {
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(searchText);
        using KustoPerformanceTrace.OperationScope measurement = KustoPerformanceTrace.Measure(
            "results.view.apply",
            sourceRows.Count);
        IEnumerable<KustoResultRowViewModel> rows = sourceRows;

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            rows = rows.Where(row => row.Cells.Any(cell =>
                cell.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (KustoResultColumnViewModel column in columns.Where(column => column.IsFilterActive))
        {
            rows = rows.Where(row => MatchesFilter(row.Cells[column.ColumnIndex].Text, column));
        }

        IOrderedEnumerable<KustoResultRowViewModel>? orderedRows = null;
        foreach (KustoResultColumnViewModel sortColumn in columns
            .Where(column => column.IsSortActive)
            .OrderBy(column => column.SortPriority))
        {
            orderedRows = ApplySort(rows, orderedRows, sortColumn);
        }

        if (orderedRows is not null)
        {
            rows = orderedRows.ThenBy(row => row.RowIndex);
        }

        return Array.AsReadOnly(rows.ToArray());
    }

    private static IOrderedEnumerable<KustoResultRowViewModel> ApplySort(
        IEnumerable<KustoResultRowViewModel> rows,
        IOrderedEnumerable<KustoResultRowViewModel>? orderedRows,
        KustoResultColumnViewModel column)
    {
        string normalizedType = column.TypeName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
            ? column.TypeName[7..]
            : column.TypeName;
        return normalizedType.ToUpperInvariant() switch
        {
            "BYTE" or "SBYTE" or "INT16" or "INT32" or "INT64" or "UINT16" or "UINT32" or "UINT64"
                or "DECIMAL" or "INT" or "LONG" => ApplyParsedSort(rows, orderedRows, column, ParseDecimal),
            "DOUBLE" or "SINGLE" or "FLOAT" or "REAL" => ApplyParsedSort(rows, orderedRows, column, ParseDouble),
            "DATETIME" or "DATETIMEOFFSET" or "DATE" => ApplyParsedSort(rows, orderedRows, column, ParseDateTime),
            "TIMESPAN" or "TIME" => ApplyParsedSort(rows, orderedRows, column, ParseTimeSpan),
            "BOOLEAN" or "BOOL" => ApplyParsedSort(rows, orderedRows, column, ParseBoolean),
            "GUID" or "UUID" => ApplyParsedSort(rows, orderedRows, column, ParseGuid),
            _ => ApplyStringSort(rows, orderedRows, column),
        };
    }

    private static IOrderedEnumerable<KustoResultRowViewModel> ApplyParsedSort<T>(
        IEnumerable<KustoResultRowViewModel> rows,
        IOrderedEnumerable<KustoResultRowViewModel>? orderedRows,
        KustoResultColumnViewModel column,
        Func<string, T?> parse)
        where T : struct, IComparable<T>
    {
        ParsedSortKeyComparer<T> comparer = new();
        Func<KustoResultRowViewModel, ParsedSortKey<T>> keySelector = row =>
        {
            string text = row.Cells[column.ColumnIndex].Text;
            return new ParsedSortKey<T>(text, parse(text));
        };
        return orderedRows is null
            ? OrderRows(rows, keySelector, comparer, column.SortDirection)
            : ThenOrderRows(orderedRows, keySelector, comparer, column.SortDirection);
    }

    private static IOrderedEnumerable<KustoResultRowViewModel> ApplyStringSort(
        IEnumerable<KustoResultRowViewModel> rows,
        IOrderedEnumerable<KustoResultRowViewModel>? orderedRows,
        KustoResultColumnViewModel column)
    {
        Func<KustoResultRowViewModel, string> keySelector = row =>
            row.Cells[column.ColumnIndex].Text;
        return orderedRows is null
            ? OrderRows(rows, keySelector, StringComparer.OrdinalIgnoreCase, column.SortDirection)
            : ThenOrderRows(orderedRows, keySelector, StringComparer.OrdinalIgnoreCase, column.SortDirection);
    }

    private static IOrderedEnumerable<KustoResultRowViewModel> OrderRows<TKey>(
        IEnumerable<KustoResultRowViewModel> rows,
        Func<KustoResultRowViewModel, TKey> keySelector,
        IComparer<TKey> comparer,
        KustoResultSortDirection direction)
    {
        return direction == KustoResultSortDirection.Ascending
            ? rows.OrderBy(keySelector, comparer)
            : rows.OrderByDescending(keySelector, comparer);
    }

    private static IOrderedEnumerable<KustoResultRowViewModel> ThenOrderRows<TKey>(
        IOrderedEnumerable<KustoResultRowViewModel> rows,
        Func<KustoResultRowViewModel, TKey> keySelector,
        IComparer<TKey> comparer,
        KustoResultSortDirection direction)
    {
        return direction == KustoResultSortDirection.Ascending
            ? rows.ThenBy(keySelector, comparer)
            : rows.ThenByDescending(keySelector, comparer);
    }

    private static bool MatchesFilter(string value, KustoResultColumnViewModel column)
    {
        string filterText = column.FilterText;
        KustoResultFilterOperator filterOperator = column.SelectedFilterOption.Operator;
        int comparison = KustoResultValueComparer.Compare(value, filterText, column.TypeName);
        return filterOperator switch
        {
            KustoResultFilterOperator.Equals => comparison == 0,
            KustoResultFilterOperator.NotEquals => comparison != 0,
            KustoResultFilterOperator.Contains => value.Contains(filterText, StringComparison.OrdinalIgnoreCase),
            KustoResultFilterOperator.DoesNotContain => !value.Contains(filterText, StringComparison.OrdinalIgnoreCase),
            KustoResultFilterOperator.StartsWith => value.StartsWith(filterText, StringComparison.OrdinalIgnoreCase),
            KustoResultFilterOperator.EndsWith => value.EndsWith(filterText, StringComparison.OrdinalIgnoreCase),
            KustoResultFilterOperator.GreaterThan => comparison > 0,
            KustoResultFilterOperator.GreaterThanOrEqual => comparison >= 0,
            KustoResultFilterOperator.LessThan => comparison < 0,
            KustoResultFilterOperator.LessThanOrEqual => comparison <= 0,
            KustoResultFilterOperator.IsEmpty => string.IsNullOrWhiteSpace(value),
            KustoResultFilterOperator.IsNotEmpty => !string.IsNullOrWhiteSpace(value),
            _ => false,
        };
    }

    private static bool? ParseBoolean(string text)
    {
        return bool.TryParse(text, out bool value) ? value : null;
    }

    private static DateTimeOffset? ParseDateTime(string text)
    {
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset value)
                ? value
                : null;
    }

    private static decimal? ParseDecimal(string text)
    {
        const NumberStyles Styles = NumberStyles.Number | NumberStyles.AllowExponent;
        return decimal.TryParse(text, Styles, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }

    private static double? ParseDouble(string text)
    {
        const NumberStyles Styles = NumberStyles.Float | NumberStyles.AllowThousands;
        return double.TryParse(text, Styles, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    private static Guid? ParseGuid(string text)
    {
        return Guid.TryParse(text, out Guid value) ? value : null;
    }

    private static TimeSpan? ParseTimeSpan(string text)
    {
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan value)
            ? value
            : null;
    }

    private readonly record struct ParsedSortKey<T>(string Text, T? Value)
        where T : struct, IComparable<T>;

    private static class KustoResultValueComparer
    {
        internal static int Compare(string left, string right, string typeName)
        {
            string normalizedType = typeName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                ? typeName[7..]
                : typeName;
            return normalizedType.ToUpperInvariant() switch
            {
                "BYTE" or "SBYTE" or "INT16" or "INT32" or "INT64" or "UINT16" or "UINT32" or "UINT64"
                    or "DECIMAL" or "INT" or "LONG" => CompareDecimal(left, right),
                "DOUBLE" or "SINGLE" or "FLOAT" or "REAL" => CompareDouble(left, right),
                "DATETIME" or "DATETIMEOFFSET" or "DATE" => CompareDateTime(left, right),
                "TIMESPAN" or "TIME" => CompareTimeSpan(left, right),
                "BOOLEAN" or "BOOL" => CompareBoolean(left, right),
                "GUID" or "UUID" => CompareGuid(left, right),
                _ => StringComparer.OrdinalIgnoreCase.Compare(left, right),
            };
        }

        private static int CompareBoolean(string left, string right)
        {
            return bool.TryParse(left, out bool leftValue) && bool.TryParse(right, out bool rightValue)
                ? leftValue.CompareTo(rightValue)
                : StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static int CompareDateTime(string left, string right)
        {
            return DateTimeOffset.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset leftValue)
                && DateTimeOffset.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset rightValue)
                    ? leftValue.CompareTo(rightValue)
                    : StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static int CompareDecimal(string left, string right)
        {
            const NumberStyles Styles = NumberStyles.Number | NumberStyles.AllowExponent;
            return decimal.TryParse(left, Styles, CultureInfo.InvariantCulture, out decimal leftValue)
                && decimal.TryParse(right, Styles, CultureInfo.InvariantCulture, out decimal rightValue)
                    ? leftValue.CompareTo(rightValue)
                    : StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static int CompareDouble(string left, string right)
        {
            const NumberStyles Styles = NumberStyles.Float | NumberStyles.AllowThousands;
            return double.TryParse(left, Styles, CultureInfo.InvariantCulture, out double leftValue)
                && double.TryParse(right, Styles, CultureInfo.InvariantCulture, out double rightValue)
                    ? leftValue.CompareTo(rightValue)
                    : StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static int CompareGuid(string left, string right)
        {
            return Guid.TryParse(left, out Guid leftValue) && Guid.TryParse(right, out Guid rightValue)
                ? leftValue.CompareTo(rightValue)
                : StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static int CompareTimeSpan(string left, string right)
        {
            return TimeSpan.TryParse(left, CultureInfo.InvariantCulture, out TimeSpan leftValue)
                && TimeSpan.TryParse(right, CultureInfo.InvariantCulture, out TimeSpan rightValue)
                    ? leftValue.CompareTo(rightValue)
                    : StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }
    }

    private sealed class ParsedSortKeyComparer<T> : IComparer<ParsedSortKey<T>>
        where T : struct, IComparable<T>
    {
        public int Compare(ParsedSortKey<T> left, ParsedSortKey<T> right)
        {
            return left.Value.HasValue && right.Value.HasValue
                ? left.Value.Value.CompareTo(right.Value.Value)
                : StringComparer.OrdinalIgnoreCase.Compare(left.Text, right.Text);
        }
    }
}
