using System.Text.Json;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Execution;
using static OpenKustoExplorer.Portable.Storage.JsonElementReader;

namespace OpenKustoExplorer.Portable.Storage;

/// <summary>
/// Reads and writes dashboard catalogs with explicit Native-AOT-safe JSON handling.
/// </summary>
public static class KustoDashboardCatalogJson
{
    private const int CurrentVersion = 2;
    private const int MinimumSupportedVersion = 1;

    /// <summary>
    /// Reads a dashboard catalog from UTF-8 JSON.
    /// </summary>
    /// <param name="stream">The readable JSON stream.</param>
    /// <returns>The dashboard catalog.</returns>
    public static KustoDashboardCatalog Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        int version = root.GetProperty("version").GetInt32();

        if (version is < MinimumSupportedVersion or > CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported dashboard catalog version {version}.");
        }

        List<KustoDashboard> dashboards = [];
        foreach (JsonElement dashboardElement in root.GetProperty("dashboards").EnumerateArray())
        {
            dashboards.Add(ReadDashboard(dashboardElement, version));
        }

        return new KustoDashboardCatalog(dashboards);
    }

    /// <summary>
    /// Writes a dashboard catalog as UTF-8 JSON.
    /// </summary>
    /// <param name="stream">The writable destination stream.</param>
    /// <param name="catalog">The dashboard catalog.</param>
    public static void Write(Stream stream, KustoDashboardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(catalog);
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", CurrentVersion);
        writer.WritePropertyName("dashboards");
        writer.WriteStartArray();

        foreach (KustoDashboard dashboard in catalog.Dashboards)
        {
            WriteDashboard(writer, dashboard);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static KustoDashboard ReadDashboard(JsonElement element, int version)
    {
        List<KustoDashboardWidget> widgets = [];
        foreach (JsonElement widgetElement in element.GetProperty("widgets").EnumerateArray())
        {
            widgets.Add(ReadWidget(widgetElement));
        }

        return new KustoDashboard(
            element.GetProperty("id").GetGuid(),
            GetRequiredString(element, "title"),
            GetRequiredString(element, "backgroundColor"),
            widgets,
            ReadTimeRange(element, version));
    }

    private static KustoDashboardTimeRange ReadTimeRange(JsonElement dashboardElement, int version)
    {
        if (version == 1)
        {
            return KustoDashboardTimeRange.Last24Hours;
        }

        if (!dashboardElement.TryGetProperty("timeRange", out JsonElement element)
            || element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Dashboard catalog version 2 requires a timeRange object.");
        }

        string kindText = GetRequiredString(element, "kind");
        if (!Enum.TryParse(kindText, true, out KustoDashboardTimeRangeKind kind)
            || !Enum.IsDefined(kind))
        {
            throw new InvalidDataException($"Unsupported dashboard time range kind {kindText}.");
        }

        return kind switch
        {
            KustoDashboardTimeRangeKind.Relative => KustoDashboardTimeRange.CreateRelative(
                TimeSpan.FromSeconds(element.GetProperty("durationSeconds").GetDouble())),
            KustoDashboardTimeRangeKind.Absolute => KustoDashboardTimeRange.CreateAbsolute(
                element.GetProperty("startUtc").GetDateTimeOffset(),
                element.GetProperty("endUtc").GetDateTimeOffset()),
            _ => throw new InvalidDataException($"Unsupported dashboard time range kind {kindText}."),
        };
    }

    private static KustoDashboardWidget ReadWidget(JsonElement element)
    {
        string displayModeText = GetRequiredString(element, "displayMode");
        string visualizationKindText = GetRequiredString(element, "visualizationKind");

        if (!Enum.TryParse(displayModeText, true, out KustoDashboardWidgetDisplayMode displayMode)
            || !Enum.IsDefined(displayMode))
        {
            throw new InvalidDataException($"Unsupported dashboard widget display mode {displayModeText}.");
        }

        if (!Enum.TryParse(visualizationKindText, true, out KustoVisualizationKind visualizationKind)
            || !Enum.IsDefined(visualizationKind))
        {
            throw new InvalidDataException($"Unsupported dashboard visualization {visualizationKindText}.");
        }

        JsonElement layoutElement = element.GetProperty("layout");
        (KustoQueryResult? CachedResult, DateTimeOffset? CachedAtUtc) cache = ReadCache(element);
        return new KustoDashboardWidget(
            element.GetProperty("id").GetGuid(),
            GetRequiredString(element, "title"),
            new Uri(GetRequiredString(element, "clusterUri"), UriKind.Absolute),
            GetRequiredString(element, "databaseName"),
            GetRequiredString(element, "queryText"),
            TimeSpan.FromSeconds(element.GetProperty("refreshIntervalSeconds").GetDouble()),
            displayMode,
            visualizationKind,
            new KustoDashboardWidgetLayout(
                layoutElement.GetProperty("column").GetInt32(),
                layoutElement.GetProperty("row").GetInt32(),
                layoutElement.GetProperty("columnSpan").GetInt32(),
                layoutElement.GetProperty("rowSpan").GetInt32()),
            GetRequiredString(element, "backgroundColor"),
            GetRequiredString(element, "foregroundColor"),
            GetRequiredString(element, "accentColor"),
            cache.CachedResult,
            cache.CachedAtUtc);
    }

    private static void WriteDashboard(Utf8JsonWriter writer, KustoDashboard dashboard)
    {
        writer.WriteStartObject();
        writer.WriteString("id", dashboard.Id);
        writer.WriteString("title", dashboard.Title);
        writer.WriteString("backgroundColor", dashboard.BackgroundColor);
        WriteTimeRange(writer, dashboard.TimeRange);
        writer.WritePropertyName("widgets");
        writer.WriteStartArray();

        foreach (KustoDashboardWidget widget in dashboard.Widgets)
        {
            WriteWidget(writer, widget);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteTimeRange(Utf8JsonWriter writer, KustoDashboardTimeRange timeRange)
    {
        writer.WritePropertyName("timeRange");
        writer.WriteStartObject();
        writer.WriteString("kind", timeRange.Kind.ToString());

        if (timeRange.Kind == KustoDashboardTimeRangeKind.Relative)
        {
            writer.WriteNumber("durationSeconds", timeRange.RelativeDuration!.Value.TotalSeconds);
        }
        else
        {
            writer.WriteString("startUtc", timeRange.StartUtc!.Value);
            writer.WriteString("endUtc", timeRange.EndUtc!.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteWidget(Utf8JsonWriter writer, KustoDashboardWidget widget)
    {
        writer.WriteStartObject();
        writer.WriteString("id", widget.Id);
        writer.WriteString("title", widget.Title);
        writer.WriteString("clusterUri", widget.ClusterUri.AbsoluteUri);
        writer.WriteString("databaseName", widget.DatabaseName);
        writer.WriteString("queryText", widget.QueryText);
        writer.WriteNumber("refreshIntervalSeconds", widget.RefreshInterval.TotalSeconds);
        writer.WriteString("displayMode", widget.DisplayMode.ToString());
        writer.WriteString("visualizationKind", widget.VisualizationKind.ToString());
        writer.WritePropertyName("layout");
        writer.WriteStartObject();
        writer.WriteNumber("column", widget.Layout.Column);
        writer.WriteNumber("row", widget.Layout.Row);
        writer.WriteNumber("columnSpan", widget.Layout.ColumnSpan);
        writer.WriteNumber("rowSpan", widget.Layout.RowSpan);
        writer.WriteEndObject();
        writer.WriteString("backgroundColor", widget.BackgroundColor);
        writer.WriteString("foregroundColor", widget.ForegroundColor);
        writer.WriteString("accentColor", widget.AccentColor);
        if (widget.CachedResult is not null && widget.CachedAtUtc is DateTimeOffset cachedAtUtc)
        {
            writer.WriteString("cachedAtUtc", cachedAtUtc);
            WriteCachedResult(writer, widget.CachedResult);
        }

        writer.WriteEndObject();
    }

    private static (KustoQueryResult? CachedResult, DateTimeOffset? CachedAtUtc) ReadCache(JsonElement element)
    {
        if (!element.TryGetProperty("cachedAtUtc", out JsonElement cachedAtElement)
            || !element.TryGetProperty("cachedResult", out JsonElement resultElement))
        {
            return (null, null);
        }

        DateTimeOffset cachedAtUtc = cachedAtElement.GetDateTimeOffset().ToUniversalTime();
        string completenessText = GetRequiredString(resultElement, "completeness");
        if (!Enum.TryParse(completenessText, true, out KustoQueryResultCompleteness completeness)
            || !Enum.IsDefined(completeness))
        {
            throw new InvalidDataException($"Unsupported cached dashboard result completeness {completenessText}.");
        }

        List<KustoResultTable> tables = [];
        foreach (JsonElement tableElement in resultElement.GetProperty("tables").EnumerateArray())
        {
            tables.Add(ReadCachedTable(tableElement));
        }

        double durationMilliseconds = resultElement.GetProperty("durationMilliseconds").GetDouble();
        return (
            new KustoQueryResult(
                tables,
                TimeSpan.FromMilliseconds(durationMilliseconds),
                completeness: completeness),
            cachedAtUtc);
    }

    private static KustoResultTable ReadCachedTable(JsonElement element)
    {
        KustoResultColumn[] columns = element.GetProperty("columns")
            .EnumerateArray()
            .Select(ReadCachedColumn)
            .ToArray();
        KustoResultRow[] rows = element.GetProperty("rows")
            .EnumerateArray()
            .Select(rowElement => ReadCachedRow(rowElement, columns.Length))
            .ToArray();
        return new KustoResultTable(GetRequiredString(element, "name"), columns, rows);
    }

    private static KustoResultColumn ReadCachedColumn(JsonElement element)
    {
        return new KustoResultColumn(
            GetRequiredString(element, "name"),
            GetRequiredString(element, "typeName"));
    }

    private static KustoResultRow ReadCachedRow(JsonElement element, int columnCount)
    {
        KustoResultValue[] values = element.EnumerateArray()
            .Select(ReadCachedValue)
            .ToArray();
        if (values.Length != columnCount)
        {
            throw new InvalidDataException("A cached dashboard row does not match its column count.");
        }

        return new KustoResultRow(values);
    }

    private static KustoResultValue ReadCachedValue(JsonElement element)
    {
        string displayText = element.GetProperty("displayText").GetString()
            ?? throw new InvalidDataException("A cached dashboard value is missing display text.");
        string? rawJson = element.TryGetProperty("rawJson", out JsonElement rawJsonElement)
            ? rawJsonElement.GetString()
            : null;
        return new KustoResultValue(
            displayText,
            rawJson,
            element.GetProperty("isNull").GetBoolean());
    }

    private static void WriteCachedResult(Utf8JsonWriter writer, KustoQueryResult result)
    {
        writer.WritePropertyName("cachedResult");
        writer.WriteStartObject();
        writer.WriteNumber("durationMilliseconds", result.Duration.TotalMilliseconds);
        writer.WriteString("completeness", result.Completeness.ToString());
        writer.WritePropertyName("tables");
        writer.WriteStartArray();
        foreach (KustoResultTable table in result.Tables)
        {
            WriteCachedTable(writer, table);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCachedTable(Utf8JsonWriter writer, KustoResultTable table)
    {
        writer.WriteStartObject();
        writer.WriteString("name", table.Name);
        writer.WritePropertyName("columns");
        writer.WriteStartArray();
        foreach (KustoResultColumn column in table.Columns)
        {
            WriteCachedColumn(writer, column);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("rows");
        writer.WriteStartArray();
        foreach (KustoResultRow row in table.Rows)
        {
            WriteCachedRow(writer, row);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCachedColumn(Utf8JsonWriter writer, KustoResultColumn column)
    {
        writer.WriteStartObject();
        writer.WriteString("name", column.Name);
        writer.WriteString("typeName", column.TypeName);
        writer.WriteEndObject();
    }

    private static void WriteCachedRow(Utf8JsonWriter writer, KustoResultRow row)
    {
        writer.WriteStartArray();
        foreach (KustoResultValue value in row.ResultValues)
        {
            WriteCachedValue(writer, value);
        }

        writer.WriteEndArray();
    }

    private static void WriteCachedValue(Utf8JsonWriter writer, KustoResultValue value)
    {
        writer.WriteStartObject();
        writer.WriteString("displayText", value.DisplayText);
        if (value.RawJson is not null)
        {
            writer.WriteString("rawJson", value.RawJson);
        }

        writer.WriteBoolean("isNull", value.IsNull);
        writer.WriteEndObject();
    }
}
