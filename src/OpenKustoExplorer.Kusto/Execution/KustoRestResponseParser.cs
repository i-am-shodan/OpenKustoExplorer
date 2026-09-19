using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Kusto.Execution;

/// <summary>
/// Parses Kusto v1 REST responses without reflection-based serialization.
/// </summary>
internal static class KustoRestResponseParser
{
    /// <summary>
    /// Parses all user result tables while enforcing a total row limit.
    /// </summary>
    /// <param name="json">The successful Kusto REST response JSON.</param>
    /// <param name="duration">The measured request duration.</param>
    /// <param name="maximumRowCount">The maximum rows retained across all result tables.</param>
    /// <returns>The immutable materialized query result.</returns>
    public static KustoQueryResult Parse(string json, TimeSpan duration, int maximumRowCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration cannot be negative.");
        }

        IReadOnlyList<KustoResultTable> tables = ParseTables(json, maximumRowCount);
        KustoVisualization? visualization = ParseVisualization(json);
        KustoQueryResultCompleteness completeness = tables.Sum(table => table.Rows.Count) >= maximumRowCount
            ? KustoQueryResultCompleteness.RecordLimitReached
            : KustoQueryResultCompleteness.Complete;
        return new KustoQueryResult(tables, duration, visualization, completeness);
    }

    /// <summary>
    /// Parses user result tables while enforcing a total row limit.
    /// </summary>
    /// <param name="json">The successful Kusto REST response JSON.</param>
    /// <param name="maximumRowCount">The maximum rows retained across all result tables.</param>
    /// <returns>The immutable materialized result tables.</returns>
    public static IReadOnlyList<KustoResultTable> ParseTables(string json, int maximumRowCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRowCount);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement tablesElement = document.RootElement.GetProperty("Tables");
        Dictionary<int, ResponseTableDescriptor> descriptors = GetTableDescriptors(tablesElement);
        string? partialFailure = FindPartialFailure(tablesElement, descriptors);
        if (partialFailure is not null)
        {
            throw new InvalidOperationException($"Kusto query failed: {partialFailure}");
        }

        List<KustoResultTable> tables = [];
        int remainingRowCount = maximumRowCount;
        int tableOrdinal = 0;

        foreach (JsonElement tableElement in tablesElement.EnumerateArray())
        {
            string tableName = tableElement.GetProperty("TableName").GetString() ?? "Result";
            descriptors.TryGetValue(tableOrdinal, out ResponseTableDescriptor? descriptor);
            bool isUserResult = descriptor is not null
                ? string.Equals(descriptor.Kind, "QueryResult", StringComparison.OrdinalIgnoreCase)
                : descriptors.Count == 0 && !IsMetadataTable(tableName);
            if (remainingRowCount > 0 && isUserResult && !IsTableOfContents(tableElement))
            {
                tables.Add(ParseTable(
                    tableElement,
                    descriptor?.Name ?? tableName,
                    ref remainingRowCount));
            }

            tableOrdinal++;
        }

        return tables.AsReadOnly();
    }

    /// <summary>
    /// Extracts a concise service error from a failed Kusto REST response.
    /// </summary>
    /// <param name="json">The failed response body.</param>
    /// <param name="fallbackMessage">The HTTP status fallback.</param>
    /// <returns>The most specific available service error.</returns>
    public static string ParseError(string json, string fallbackMessage)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackMessage);

        string errorMessage = fallbackMessage;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                errorMessage = GetErrorMessage(error) ?? errorMessage;
            }
            else if (root.TryGetProperty("@message", out JsonElement message))
            {
                errorMessage = message.GetString() ?? errorMessage;
            }
        }
        catch (JsonException)
        {
            // Some gateways return plain text; the HTTP status remains safe and actionable.
        }

        return errorMessage;
    }

    private static string? FindPartialFailure(
        JsonElement tablesElement,
        IReadOnlyDictionary<int, ResponseTableDescriptor> descriptors)
    {
        string? failure = null;
        int tableOrdinal = 0;

        foreach (JsonElement tableElement in tablesElement.EnumerateArray())
        {
            string tableName = tableElement.GetProperty("TableName").GetString() ?? string.Empty;
            descriptors.TryGetValue(tableOrdinal, out ResponseTableDescriptor? descriptor);
            if (string.Equals(tableName, "QueryStatus", StringComparison.Ordinal)
                || string.Equals(descriptor?.Kind, "QueryStatus", StringComparison.OrdinalIgnoreCase)
                || string.Equals(descriptor?.Name, "QueryStatus", StringComparison.OrdinalIgnoreCase))
            {
                failure = FindQueryStatusFailure(tableElement);
            }

            tableOrdinal++;
        }

        return failure;
    }

    private static string? FindQueryStatusFailure(JsonElement tableElement)
    {
        Dictionary<string, int> columnIndexes = GetColumnIndexes(tableElement);
        bool hasSeverity = columnIndexes.TryGetValue("Severity", out int severityIndex);
        bool hasDescription = columnIndexes.TryGetValue("StatusDescription", out int descriptionIndex);
        string? failure = null;

        if (hasSeverity && hasDescription)
        {
            foreach (JsonElement row in tableElement.GetProperty("Rows").EnumerateArray())
            {
                JsonElement[] values = row.EnumerateArray().ToArray();
                if (values[severityIndex].TryGetInt32(out int severity) && severity <= 2)
                {
                    failure = FormatValue(values[descriptionIndex]);
                }
            }
        }

        return failure;
    }

    private static Dictionary<string, int> GetColumnIndexes(JsonElement tableElement)
    {
        Dictionary<string, int> columnIndexes = new(StringComparer.OrdinalIgnoreCase);
        int columnIndex = 0;

        foreach (JsonElement column in tableElement.GetProperty("Columns").EnumerateArray())
        {
            string columnName = column.GetProperty("ColumnName").GetString() ?? string.Empty;
            columnIndexes[columnName] = columnIndex;
            columnIndex++;
        }

        return columnIndexes;
    }

    private static Dictionary<int, ResponseTableDescriptor> GetTableDescriptors(
        JsonElement tablesElement)
    {
        Dictionary<int, ResponseTableDescriptor> descriptors = [];
        foreach (JsonElement tableElement in tablesElement.EnumerateArray().Where(IsTableOfContents))
        {
            Dictionary<string, int> columnIndexes = GetColumnIndexes(tableElement);
            int ordinalIndex = columnIndexes["Ordinal"];
            int kindIndex = columnIndexes["Kind"];
            int nameIndex = columnIndexes["Name"];
            foreach (JsonElement row in tableElement.GetProperty("Rows").EnumerateArray())
            {
                JsonElement[] values = row.EnumerateArray().ToArray();
                if (TryGetInt32(values[ordinalIndex], out int ordinal))
                {
                    descriptors[ordinal] = new ResponseTableDescriptor(
                        FormatValue(values[kindIndex]),
                        FormatValue(values[nameIndex]));
                }
            }
        }

        return descriptors;
    }

    private static bool IsTableOfContents(JsonElement tableElement)
    {
        string tableName = tableElement.GetProperty("TableName").GetString() ?? string.Empty;
        if (string.Equals(tableName, "TableOfContents", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Dictionary<string, int> columnIndexes = GetColumnIndexes(tableElement);
        return tableName.StartsWith("Table_", StringComparison.OrdinalIgnoreCase)
            && columnIndexes.ContainsKey("Ordinal")
            && columnIndexes.ContainsKey("Kind")
            && columnIndexes.ContainsKey("Name")
            && columnIndexes.ContainsKey("Id")
            && columnIndexes.ContainsKey("PrettyName");
    }

    private static bool TryGetInt32(JsonElement value, out int result)
    {
        return value.TryGetInt32(out result)
            || (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result));
    }

    private static ReadOnlyCollection<string> GetColumnList(JsonElement visualization, string propertyName)
    {
        List<string> columns = [];

        if (TryGetProperty(visualization, propertyName, out JsonElement property))
        {
            if (property.ValueKind == JsonValueKind.Array)
            {
                columns.AddRange(property
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .OfType<string>());
            }
            else if (property.ValueKind == JsonValueKind.String)
            {
                string value = property.GetString() ?? string.Empty;
                columns.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        return columns.AsReadOnly();
    }

    private static string? GetErrorMessage(JsonElement error)
    {
        string? message = null;

        if (error.TryGetProperty("@message", out JsonElement detailedMessageElement)
            && detailedMessageElement.ValueKind == JsonValueKind.String)
        {
            message = detailedMessageElement.GetString();
        }

        if (string.IsNullOrWhiteSpace(message)
            && error.TryGetProperty("message", out JsonElement messageElement))
        {
            if (messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString();
            }
            else if (messageElement.TryGetProperty("value", out JsonElement valueElement))
            {
                message = valueElement.GetString();
            }
        }

        return message;
    }

    private static bool IsMetadataTable(string tableName)
    {
        bool isMetadataTable = tableName.StartsWith('@')
            || string.Equals(tableName, "QueryStatus", StringComparison.Ordinal)
            || string.Equals(tableName, "TableOfContents", StringComparison.Ordinal)
            || string.Equals(tableName, "QueryCompletionInformation", StringComparison.Ordinal);

        return isMetadataTable;
    }

    private static KustoVisualization? ParseVisualization(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement tablesElement = document.RootElement.GetProperty("Tables");
        Dictionary<int, ResponseTableDescriptor> descriptors = GetTableDescriptors(tablesElement);
        KustoVisualization? visualization = null;
        int tableOrdinal = 0;

        foreach (JsonElement tableElement in tablesElement.EnumerateArray())
        {
            string tableName = tableElement.GetProperty("TableName").GetString() ?? string.Empty;
            descriptors.TryGetValue(tableOrdinal, out ResponseTableDescriptor? descriptor);
            if (string.Equals(tableName, "@ExtendedProperties", StringComparison.OrdinalIgnoreCase)
                || string.Equals(descriptor?.Name, "@ExtendedProperties", StringComparison.OrdinalIgnoreCase))
            {
                visualization = ParseVisualizationTable(tableElement) ?? visualization;
            }

            tableOrdinal++;
        }

        return visualization;
    }

    private static KustoVisualization? ParseVisualizationElement(JsonElement element)
    {
        KustoVisualization? visualization = null;
        string? visualizationName = GetString(element, "Visualization");

        if (visualizationName is not null && KustoVisualization.TryParseKind(visualizationName, out KustoVisualizationKind kind))
        {
            string? legend = GetString(element, "Legend");
            visualization = new KustoVisualization(
                kind,
                GetString(element, "Title"),
                GetString(element, "XColumn"),
                GetString(element, "XTitle"),
                GetString(element, "YTitle"),
                GetColumnList(element, "Series"),
                GetColumnList(element, "YColumns"),
                GetColumnList(element, "AnomalyColumns"),
                GetString(element, "Kind"),
                !string.Equals(legend, "hidden", StringComparison.OrdinalIgnoreCase),
                GetBoolean(element, "Accumulate"),
                GetDouble(element, "YMin"),
                GetDouble(element, "YMax"),
                string.Equals(GetString(element, "XAxis"), "log", StringComparison.OrdinalIgnoreCase),
                string.Equals(GetString(element, "YAxis"), "log", StringComparison.OrdinalIgnoreCase),
                GetString(element, "YSplit"));
        }

        return visualization;
    }

    private static KustoVisualization? ParseVisualizationTable(JsonElement tableElement)
    {
        Dictionary<string, int> columnIndexes = GetColumnIndexes(tableElement);
        bool hasValue = columnIndexes.TryGetValue("Value", out int valueIndex);
        bool hasKey = columnIndexes.TryGetValue("Key", out int keyIndex);
        KustoVisualization? visualization = null;

        if (hasValue)
        {
            foreach (JsonElement row in tableElement.GetProperty("Rows").EnumerateArray())
            {
                JsonElement[] values = row.EnumerateArray().ToArray();
                bool isVisualization = !hasKey
                    || string.Equals(FormatValue(values[keyIndex]), "Visualization", StringComparison.OrdinalIgnoreCase);
                if (isVisualization)
                {
                    visualization = ParseVisualizationValue(values[valueIndex]) ?? visualization;
                }
            }
        }

        return visualization;
    }

    private static KustoVisualization? ParseVisualizationValue(JsonElement value)
    {
        KustoVisualization? visualization = null;

        if (value.ValueKind == JsonValueKind.Object)
        {
            visualization = ParseVisualizationElement(value);
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString() ?? string.Empty;
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                visualization = document.RootElement.ValueKind == JsonValueKind.Object
                    ? ParseVisualizationElement(document.RootElement)
                    : null;
            }
            catch (JsonException)
            {
                // Unknown extended properties remain safely ignored.
            }
        }

        return visualization;
    }

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        bool value = false;

        if (TryGetProperty(element, propertyName, out JsonElement property))
        {
            value = property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.String => bool.TryParse(property.GetString(), out bool parsed) && parsed,
                _ => false,
            };
        }

        return value;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        double? value = null;

        if (TryGetProperty(element, propertyName, out JsonElement property))
        {
            double number = 0;
            bool parsed = (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out number))
                || (property.ValueKind == JsonValueKind.String
                && double.TryParse(
                    property.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out number));
            if (parsed && double.IsFinite(number))
            {
                value = number;
            }
        }

        return value;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        string? value = null;
        if (TryGetProperty(element, propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        bool found = false;

        foreach (JsonProperty property in element
            .EnumerateObject()
            .Where(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)))
        {
            value = property.Value;
            found = true;
        }

        return found;
    }

    private static KustoResultTable ParseTable(
        JsonElement tableElement,
        string tableName,
        ref int remainingRowCount)
    {
        KustoResultColumn[] columns = tableElement
            .GetProperty("Columns")
            .EnumerateArray()
            .Select(ParseColumn)
            .ToArray();
        List<KustoResultRow> rows = [];

        foreach (JsonElement rowElement in tableElement
            .GetProperty("Rows")
            .EnumerateArray()
            .Take(remainingRowCount))
        {
            KustoResultValue[] values = rowElement
                .EnumerateArray()
                .Select(value => new KustoResultValue(
                    FormatValue(value),
                    value.GetRawText(),
                    value.ValueKind == JsonValueKind.Null))
                .ToArray();
            rows.Add(new KustoResultRow(values));
            remainingRowCount--;
        }

        return new KustoResultTable(tableName, columns, rows);
    }

    private static KustoResultColumn ParseColumn(JsonElement columnElement)
    {
        string name = columnElement.GetProperty("ColumnName").GetString() ?? "Column";
        string typeName = columnElement.GetProperty("ColumnType").GetString()
            ?? columnElement.GetProperty("DataType").GetString()
            ?? "dynamic";

        return new KustoResultColumn(name, typeName);
    }

    private static string FormatValue(JsonElement value)
    {
        string displayValue = value.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText(),
        };

        return displayValue;
    }

    private sealed record ResponseTableDescriptor(string Kind, string Name);
}
