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
            KustoResultValueComparer comparer = new(sortColumn.TypeName);
            Func<KustoResultRowViewModel, string> keySelector = row =>
                row.Cells[sortColumn.ColumnIndex].Text;
            orderedRows = orderedRows is null
                ? OrderRows(rows, keySelector, comparer, sortColumn.SortDirection)
                : ThenOrderRows(orderedRows, keySelector, comparer, sortColumn.SortDirection);
        }

        if (orderedRows is not null)
        {
            rows = orderedRows.ThenBy(row => row.RowIndex);
        }

        return Array.AsReadOnly(rows.ToArray());
    }

    private static IOrderedEnumerable<KustoResultRowViewModel> OrderRows(
        IEnumerable<KustoResultRowViewModel> rows,
        Func<KustoResultRowViewModel, string> keySelector,
        IComparer<string> comparer,
        KustoResultSortDirection direction)
    {
        return direction == KustoResultSortDirection.Ascending
            ? rows.OrderBy(keySelector, comparer)
            : rows.OrderByDescending(keySelector, comparer);
    }

    private static IOrderedEnumerable<KustoResultRowViewModel> ThenOrderRows(
        IOrderedEnumerable<KustoResultRowViewModel> rows,
        Func<KustoResultRowViewModel, string> keySelector,
        IComparer<string> comparer,
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

    private sealed class KustoResultValueComparer : IComparer<string>
    {
        private readonly string typeName;

        internal KustoResultValueComparer(string typeName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
            this.typeName = typeName;
        }

        public int Compare(string? left, string? right)
        {
            return Compare(left ?? string.Empty, right ?? string.Empty, typeName);
        }

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
}
