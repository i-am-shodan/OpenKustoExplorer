using System.Collections.ObjectModel;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Kusto.Connections;

/// <summary>
/// Parses the tabular result returned by the Kusto <c>.show functions</c> command.
/// </summary>
internal static class KustoFunctionResultParser
{
    /// <summary>
    /// Parses stored functions from management result tables.
    /// </summary>
    /// <param name="tables">The materialized management result tables.</param>
    /// <returns>The stored functions in server order.</returns>
    public static ReadOnlyCollection<KustoFunctionSchema> Parse(IReadOnlyList<KustoResultTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        List<KustoFunctionSchema> functions = [];
        foreach (KustoResultTable table in tables.Where(HasFunctionColumns))
        {
            Dictionary<string, int> columnIndexes = table.Columns
                .Select((column, index) => (column.Name, Index: index))
                .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

            foreach (IReadOnlyList<string> values in table.Rows.Select(row => row.Values))
            {
                string name = GetValue(values, columnIndexes, "Name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    functions.Add(new KustoFunctionSchema(
                        name,
                        GetValue(values, columnIndexes, "Parameters"),
                        GetValue(values, columnIndexes, "Body"),
                        GetValue(values, columnIndexes, "Folder"),
                        GetValue(values, columnIndexes, "DocString")));
                }
            }
        }

        return functions.AsReadOnly();
    }

    private static string GetValue(
        IReadOnlyList<string> values,
        Dictionary<string, int> columnIndexes,
        string columnName)
    {
        string value = string.Empty;
        if (columnIndexes.TryGetValue(columnName, out int index) && index < values.Count)
        {
            value = values[index];
        }

        return value;
    }

    private static bool HasFunctionColumns(KustoResultTable table)
    {
        bool hasName = table.Columns.Any(column => string.Equals(
            column.Name,
            "Name",
            StringComparison.OrdinalIgnoreCase));
        bool hasParameters = table.Columns.Any(column => string.Equals(
            column.Name,
            "Parameters",
            StringComparison.OrdinalIgnoreCase));

        return hasName && hasParameters;
    }
}
