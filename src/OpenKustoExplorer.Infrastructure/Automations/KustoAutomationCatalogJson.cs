using System.Globalization;
using System.Text.Json;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Execution;
using static OpenKustoExplorer.Infrastructure.Storage.JsonElementReader;

namespace OpenKustoExplorer.Infrastructure.Automations;

/// <summary>
/// Reads and writes automation catalogs with explicit Native-AOT-safe JSON handling.
/// </summary>
internal static class KustoAutomationCatalogJson
{
    private const int CurrentVersion = 1;

    /// <summary>
    /// Reads an automation catalog from UTF-8 JSON.
    /// </summary>
    /// <param name="stream">The readable JSON stream.</param>
    /// <returns>The immutable automation catalog.</returns>
    public static KustoAutomationCatalog Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        int version = root.GetProperty("version").GetInt32();
        if (version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported automation catalog version {version}.");
        }

        List<KustoAutomation> automations = [];
        foreach (JsonElement automationElement in root.GetProperty("automations").EnumerateArray())
        {
            automations.Add(ReadAutomation(automationElement));
        }

        return new KustoAutomationCatalog(automations);
    }

    /// <summary>
    /// Writes an automation catalog as UTF-8 JSON.
    /// </summary>
    /// <param name="stream">The writable destination stream.</param>
    /// <param name="catalog">The immutable automation catalog.</param>
    public static void Write(Stream stream, KustoAutomationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(catalog);

        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", CurrentVersion);
        writer.WritePropertyName("automations");
        writer.WriteStartArray();

        foreach (KustoAutomation automation in catalog.Automations)
        {
            WriteAutomation(writer, automation);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static KustoAutomation ReadAutomation(JsonElement element)
    {
        List<KustoAutomationRun> runs = [];
        foreach (JsonElement runElement in element.GetProperty("runs").EnumerateArray())
        {
            runs.Add(ReadRun(runElement));
        }

        return new KustoAutomation(
            element.GetProperty("id").GetGuid(),
            GetRequiredString(element, "name"),
            new Uri(GetRequiredString(element, "clusterUri"), UriKind.Absolute),
            GetRequiredString(element, "databaseName"),
            GetRequiredString(element, "queryText"),
            TimeSpan.FromSeconds(element.GetProperty("intervalSeconds").GetDouble()),
            element.GetProperty("createdAtUtc").GetDateTimeOffset(),
            element.GetProperty("nextRunAtUtc").GetDateTimeOffset(),
            GetOptionalDateTimeOffset(element, "stopAtUtc"),
            element.GetProperty("isEnabled").GetBoolean(),
            runs,
            ReadNotificationSettings(element));
    }

    private static KustoAutomationNotificationSettings ReadNotificationSettings(JsonElement automationElement)
    {
        KustoAutomationNotificationSettings settings = KustoAutomationNotificationSettings.Disabled;

        if (automationElement.TryGetProperty("notifications", out JsonElement element)
            && element.ValueKind == JsonValueKind.Object)
        {
            string comparisonText = GetOptionalString(element, "rowCountComparison")
                ?? nameof(KustoAutomationRowCountComparison.None);
            if (!Enum.TryParse(
                comparisonText,
                ignoreCase: true,
                out KustoAutomationRowCountComparison comparison)
                || !Enum.IsDefined(comparison))
            {
                throw new InvalidDataException($"Unsupported automation row-count comparison {comparisonText}.");
            }

            string subjectTemplate = GetOptionalString(element, "subjectTemplate")
                ?? KustoAutomationNotificationSettings.DefaultSubjectTemplate;
            string messageTemplate = GetOptionalString(element, "messageTemplate")
                ?? KustoAutomationNotificationSettings.DefaultMessageTemplate;
            settings = new KustoAutomationNotificationSettings(
                GetOptionalBoolean(element, "notifyWhenRowCountChanges", defaultValue: false),
                comparison,
                GetOptionalInt32(element, "rowCountValue", defaultValue: 0),
                GetOptionalBoolean(element, "desktopEnabled", defaultValue: false),
                GetOptionalBoolean(element, "emailEnabled", defaultValue: false),
                GetOptionalString(element, "emailRecipient"),
                GetOptionalString(element, "emailSender"),
                GetOptionalString(element, "smtpHost"),
                GetOptionalInt32(element, "smtpPort", defaultValue: 587),
                GetOptionalBoolean(element, "smtpUseSsl", defaultValue: true),
                subjectTemplate,
                messageTemplate,
                GetOptionalBoolean(element, "runApplicationEnabled", defaultValue: false),
                GetOptionalString(element, "applicationPath"),
                GetOptionalString(element, "applicationArguments"));
        }

        return settings;
    }

    private static KustoAutomationRun ReadRun(JsonElement element)
    {
        string statusText = GetRequiredString(element, "status");
        if (!Enum.TryParse(statusText, ignoreCase: true, out KustoAutomationRunStatus status)
            || !Enum.IsDefined(status))
        {
            throw new InvalidDataException($"Unsupported automation run status {statusText}.");
        }

        KustoQueryResult? result = null;
        if (element.TryGetProperty("result", out JsonElement resultElement)
            && resultElement.ValueKind == JsonValueKind.Object)
        {
            result = ReadResult(resultElement);
        }

        return new KustoAutomationRun(
            element.GetProperty("id").GetGuid(),
            element.GetProperty("startedAtUtc").GetDateTimeOffset(),
            element.GetProperty("completedAtUtc").GetDateTimeOffset(),
            status,
            GetOptionalString(element, "errorMessage"),
            result);
    }

    private static KustoQueryResult ReadResult(JsonElement element)
    {
        List<KustoResultTable> tables = [];
        foreach (JsonElement tableElement in element.GetProperty("tables").EnumerateArray())
        {
            tables.Add(ReadTable(tableElement));
        }

        KustoVisualization? visualization = null;
        if (element.TryGetProperty("visualization", out JsonElement visualizationElement)
            && visualizationElement.ValueKind == JsonValueKind.Object)
        {
            visualization = ReadVisualization(visualizationElement);
        }

        double durationMilliseconds = element.GetProperty("durationMilliseconds").GetDouble();
        return new KustoQueryResult(tables, TimeSpan.FromMilliseconds(durationMilliseconds), visualization);
    }

    private static KustoResultTable ReadTable(JsonElement element)
    {
        List<KustoResultColumn> columns = [];
        foreach (JsonElement columnElement in element.GetProperty("columns").EnumerateArray())
        {
            columns.Add(new KustoResultColumn(
                GetRequiredString(columnElement, "name"),
                GetRequiredString(columnElement, "typeName")));
        }

        List<KustoResultRow> rows = [];
        foreach (JsonElement rowElement in element.GetProperty("rows").EnumerateArray())
        {
            rows.Add(new KustoResultRow(rowElement
                .EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty)));
        }

        return new KustoResultTable(GetRequiredString(element, "name"), columns, rows);
    }

    private static KustoVisualization ReadVisualization(JsonElement element)
    {
        string kindText = GetRequiredString(element, "kind");
        if (!Enum.TryParse(kindText, ignoreCase: true, out KustoVisualizationKind kind)
            || !Enum.IsDefined(kind))
        {
            throw new InvalidDataException($"Unsupported automation visualization kind {kindText}.");
        }

        return new KustoVisualization(
            kind,
            GetOptionalString(element, "title"),
            GetOptionalString(element, "xColumn"),
            GetOptionalString(element, "xTitle"),
            GetOptionalString(element, "yTitle"),
            ReadStringArray(element, "seriesColumns"),
            ReadStringArray(element, "yColumns"),
            ReadStringArray(element, "anomalyColumns"),
            GetOptionalString(element, "kindOption"),
            GetOptionalBoolean(element, "legendVisible", defaultValue: true),
            GetOptionalBoolean(element, "accumulate", defaultValue: false),
            GetOptionalDouble(element, "yMinimum"),
            GetOptionalDouble(element, "yMaximum"),
            GetOptionalBoolean(element, "xAxisLogarithmic", defaultValue: false),
            GetOptionalBoolean(element, "yAxisLogarithmic", defaultValue: false),
            GetOptionalString(element, "ySplit"));
    }

    private static void WriteAutomation(Utf8JsonWriter writer, KustoAutomation automation)
    {
        writer.WriteStartObject();
        writer.WriteString("id", automation.Id);
        writer.WriteString("name", automation.Name);
        writer.WriteString("clusterUri", automation.ClusterUri.AbsoluteUri);
        writer.WriteString("databaseName", automation.DatabaseName);
        writer.WriteString("queryText", automation.QueryText);
        writer.WriteNumber("intervalSeconds", automation.Interval.TotalSeconds);
        writer.WriteString("createdAtUtc", automation.CreatedAtUtc);
        writer.WriteString("nextRunAtUtc", automation.NextRunAtUtc);

        if (automation.StopAtUtc is DateTimeOffset stopAtUtc)
        {
            writer.WriteString("stopAtUtc", stopAtUtc);
        }
        else
        {
            writer.WriteNull("stopAtUtc");
        }

        writer.WriteBoolean("isEnabled", automation.IsEnabled);
        WriteNotificationSettings(writer, automation.NotificationSettings);
        writer.WritePropertyName("runs");
        writer.WriteStartArray();

        foreach (KustoAutomationRun run in automation.Runs)
        {
            WriteRun(writer, run);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteNotificationSettings(
        Utf8JsonWriter writer,
        KustoAutomationNotificationSettings settings)
    {
        writer.WritePropertyName("notifications");
        writer.WriteStartObject();
        writer.WriteBoolean("notifyWhenRowCountChanges", settings.NotifyWhenRowCountChanges);
        writer.WriteString("rowCountComparison", settings.RowCountComparison.ToString());
        writer.WriteNumber("rowCountValue", settings.RowCountValue);
        writer.WriteBoolean("desktopEnabled", settings.DesktopEnabled);
        writer.WriteBoolean("emailEnabled", settings.EmailEnabled);
        WriteOptionalString(writer, "emailRecipient", settings.EmailRecipient);
        WriteOptionalString(writer, "emailSender", settings.EmailSender);
        WriteOptionalString(writer, "smtpHost", settings.SmtpHost);
        writer.WriteNumber("smtpPort", settings.SmtpPort);
        writer.WriteBoolean("smtpUseSsl", settings.SmtpUseSsl);
        writer.WriteString("subjectTemplate", settings.SubjectTemplate);
        writer.WriteString("messageTemplate", settings.MessageTemplate);
        writer.WriteBoolean("runApplicationEnabled", settings.RunApplicationEnabled);
        WriteOptionalString(writer, "applicationPath", settings.ApplicationPath);
        WriteOptionalString(writer, "applicationArguments", settings.ApplicationArguments);
        writer.WriteEndObject();
    }

    private static void WriteRun(Utf8JsonWriter writer, KustoAutomationRun run)
    {
        writer.WriteStartObject();
        writer.WriteString("id", run.Id);
        writer.WriteString("startedAtUtc", run.StartedAtUtc);
        writer.WriteString("completedAtUtc", run.CompletedAtUtc);
        writer.WriteString("status", run.Status.ToString());

        if (run.ErrorMessage is not null)
        {
            writer.WriteString("errorMessage", run.ErrorMessage);
        }

        if (run.Result is not null)
        {
            writer.WritePropertyName("result");
            WriteResult(writer, run.Result);
        }

        writer.WriteEndObject();
    }

    private static void WriteResult(Utf8JsonWriter writer, KustoQueryResult result)
    {
        writer.WriteStartObject();
        writer.WriteNumber("durationMilliseconds", result.Duration.TotalMilliseconds);

        if (result.Visualization is not null)
        {
            writer.WritePropertyName("visualization");
            WriteVisualization(writer, result.Visualization);
        }

        writer.WritePropertyName("tables");
        writer.WriteStartArray();

        foreach (KustoResultTable table in result.Tables)
        {
            WriteTable(writer, table);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteTable(Utf8JsonWriter writer, KustoResultTable table)
    {
        writer.WriteStartObject();
        writer.WriteString("name", table.Name);
        writer.WritePropertyName("columns");
        writer.WriteStartArray();

        foreach (KustoResultColumn column in table.Columns)
        {
            writer.WriteStartObject();
            writer.WriteString("name", column.Name);
            writer.WriteString("typeName", column.TypeName);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("rows");
        writer.WriteStartArray();

        foreach (KustoResultRow row in table.Rows)
        {
            writer.WriteStartArray();
            foreach (string value in row.Values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteVisualization(Utf8JsonWriter writer, KustoVisualization visualization)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", visualization.Kind.ToString());
        WriteOptionalString(writer, "title", visualization.Title);
        WriteOptionalString(writer, "xColumn", visualization.XColumn);
        WriteOptionalString(writer, "xTitle", visualization.XTitle);
        WriteOptionalString(writer, "yTitle", visualization.YTitle);
        WriteStringArray(writer, "seriesColumns", visualization.SeriesColumns);
        WriteStringArray(writer, "yColumns", visualization.YColumns);
        WriteStringArray(writer, "anomalyColumns", visualization.AnomalyColumns);
        WriteOptionalString(writer, "kindOption", visualization.KindOption);
        writer.WriteBoolean("legendVisible", visualization.LegendVisible);
        writer.WriteBoolean("accumulate", visualization.Accumulate);
        WriteOptionalNumber(writer, "yMinimum", visualization.YMinimum);
        WriteOptionalNumber(writer, "yMaximum", visualization.YMaximum);
        writer.WriteBoolean("xAxisLogarithmic", visualization.XAxisLogarithmic);
        writer.WriteBoolean("yAxisLogarithmic", visualization.YAxisLogarithmic);
        WriteOptionalString(writer, "ySplit", visualization.YSplit);
        writer.WriteEndObject();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteOptionalNumber(Utf8JsonWriter writer, string propertyName, double? value)
    {
        if (value is double number)
        {
            writer.WriteNumber(propertyName, number);
        }
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<string> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();

        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
