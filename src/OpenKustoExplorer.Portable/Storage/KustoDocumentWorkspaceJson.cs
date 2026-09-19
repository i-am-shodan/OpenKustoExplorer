using System.Collections.ObjectModel;
using System.Text.Json;
using OpenKustoExplorer.Application.Documents;

namespace OpenKustoExplorer.Portable.Storage;

/// <summary>
/// Reads and writes document workspaces with explicit AOT-safe JSON handling.
/// </summary>
public static class KustoDocumentWorkspaceJson
{
    private const int CurrentVersion = 2;
    private const int MinimumSupportedVersion = 1;

    /// <summary>
    /// Reads a document workspace from JSON.
    /// </summary>
    /// <param name="stream">The readable UTF-8 JSON stream.</param>
    /// <returns>The immutable document workspace.</returns>
    public static KustoDocumentWorkspace Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        int version = root.GetProperty("version").GetInt32();
        if (version is < MinimumSupportedVersion or > CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported document workspace version {version}.");
        }

        List<KustoDocument> documents = [];
        foreach (JsonElement documentElement in root.GetProperty("documents").EnumerateArray())
        {
            documents.Add(ReadDocument(documentElement));
        }

        Guid? selectedDocumentId = null;
        if (root.TryGetProperty("selectedDocumentId", out JsonElement selectedElement)
            && selectedElement.ValueKind == JsonValueKind.String
            && selectedElement.TryGetGuid(out Guid selectedId))
        {
            selectedDocumentId = selectedId;
        }

        return new KustoDocumentWorkspace(documents, selectedDocumentId);
    }

    /// <summary>
    /// Writes a document workspace as UTF-8 JSON.
    /// </summary>
    /// <param name="stream">The writable destination stream.</param>
    /// <param name="workspace">The immutable document workspace.</param>
    public static void Write(Stream stream, KustoDocumentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(workspace);

        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", CurrentVersion);

        if (workspace.SelectedDocumentId is Guid selectedDocumentId)
        {
            writer.WriteString("selectedDocumentId", selectedDocumentId);
        }
        else
        {
            writer.WriteNull("selectedDocumentId");
        }

        writer.WritePropertyName("documents");
        writer.WriteStartArray();

        foreach (KustoDocument document in workspace.Documents)
        {
            WriteDocument(writer, document);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static KustoDocument ReadDocument(JsonElement element)
    {
        Guid id = element.GetProperty("id").GetGuid();
        string title = GetRequiredString(element, "title");
        string text = element.GetProperty("text").GetString() ?? string.Empty;
        int caretPosition = element.GetProperty("caretPosition").GetInt32();
        Uri? clusterUri = null;
        string? databaseName = null;
        KustoDocumentTabColor tabColor = ReadTabColor(element);
        string? groupName = ReadOptionalString(element, "groupName");
        bool useAlternatingRows = ReadOptionalBoolean(
            element,
            "useAlternatingRows",
            defaultValue: true);
        IReadOnlyList<KustoConditionalFormatRule> formattingRules = ReadFormattingRules(element);

        if (element.TryGetProperty("clusterUri", out JsonElement clusterElement)
            && clusterElement.ValueKind == JsonValueKind.String)
        {
            string clusterText = clusterElement.GetString() ?? string.Empty;
            clusterUri = new Uri(clusterText, UriKind.Absolute);
            databaseName = GetRequiredString(element, "databaseName");
        }

        return new KustoDocument(
            id,
            title,
            text,
            caretPosition,
            clusterUri,
            databaseName,
            tabColor,
            groupName,
            useAlternatingRows,
            formattingRules);
    }

    private static bool ReadOptionalBoolean(
        JsonElement element,
        string propertyName,
        bool defaultValue = false)
    {
        bool value = defaultValue;

        if (element.TryGetProperty(propertyName, out JsonElement propertyElement)
            && propertyElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = propertyElement.GetBoolean();
        }

        return value;
    }

    private static ReadOnlyCollection<KustoConditionalFormatRule> ReadFormattingRules(JsonElement element)
    {
        List<KustoConditionalFormatRule> rules = [];

        if (element.TryGetProperty("conditionalFormattingRules", out JsonElement rulesElement)
            && rulesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement ruleElement in rulesElement.EnumerateArray())
            {
                rules.Add(new KustoConditionalFormatRule(
                    ruleElement.GetProperty("id").GetGuid(),
                    GetRequiredString(ruleElement, "columnName"),
                    ReadComparison(ruleElement),
                    ruleElement.GetProperty("comparisonValue").GetString() ?? string.Empty,
                    ReadTarget(ruleElement),
                    GetRequiredString(ruleElement, "color")));
            }
        }

        return rules.AsReadOnly();
    }

    private static KustoConditionalFormatOperator ReadComparison(JsonElement element)
    {
        string value = GetRequiredString(element, "comparison");
        if (!Enum.TryParse(value, ignoreCase: true, out KustoConditionalFormatOperator comparison)
            || !Enum.IsDefined(comparison))
        {
            throw new InvalidDataException($"Unsupported conditional-format comparison {value}.");
        }

        return comparison;
    }

    private static KustoConditionalFormatTarget ReadTarget(JsonElement element)
    {
        string value = GetRequiredString(element, "target");
        if (!Enum.TryParse(value, ignoreCase: true, out KustoConditionalFormatTarget target)
            || !Enum.IsDefined(target))
        {
            throw new InvalidDataException($"Unsupported conditional-format target {value}.");
        }

        return target;
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        string? value = null;
        if (element.TryGetProperty(propertyName, out JsonElement propertyElement)
            && propertyElement.ValueKind == JsonValueKind.String)
        {
            value = propertyElement.GetString();
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static KustoDocumentTabColor ReadTabColor(JsonElement element)
    {
        KustoDocumentTabColor tabColor = KustoDocumentTabColor.Default;
        string? colorText = ReadOptionalString(element, "tabColor");
        if (colorText is not null
            && !Enum.TryParse(colorText, ignoreCase: true, out tabColor))
        {
            throw new InvalidDataException($"Unsupported document tab color {colorText}.");
        }

        return tabColor;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        string? value = element.GetProperty(propertyName).GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"The document workspace property {propertyName} is missing.");
        }

        return value;
    }

    private static void WriteDocument(Utf8JsonWriter writer, KustoDocument document)
    {
        writer.WriteStartObject();
        writer.WriteString("id", document.Id);
        writer.WriteString("title", document.Title);
        writer.WriteString("text", document.Text);
        writer.WriteNumber("caretPosition", document.CaretPosition);

        if (document.TabColor != KustoDocumentTabColor.Default)
        {
            writer.WriteString("tabColor", document.TabColor.ToString());
        }

        if (document.GroupName is not null)
        {
            writer.WriteString("groupName", document.GroupName);
        }

        writer.WriteBoolean("useAlternatingRows", document.UseAlternatingRows);

        if (document.ConditionalFormattingRules.Count > 0)
        {
            writer.WritePropertyName("conditionalFormattingRules");
            writer.WriteStartArray();

            foreach (KustoConditionalFormatRule rule in document.ConditionalFormattingRules)
            {
                writer.WriteStartObject();
                writer.WriteString("id", rule.Id);
                writer.WriteString("columnName", rule.ColumnName);
                writer.WriteString("comparison", rule.Comparison.ToString());
                writer.WriteString("comparisonValue", rule.ComparisonValue);
                writer.WriteString("target", rule.Target.ToString());
                writer.WriteString("color", rule.ColorHex);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (document.ClusterUri is not null)
        {
            writer.WriteString("clusterUri", document.ClusterUri.AbsoluteUri);
            writer.WriteString("databaseName", document.DatabaseName);
        }
        else
        {
            writer.WriteNull("clusterUri");
            writer.WriteNull("databaseName");
        }

        writer.WriteEndObject();
    }
}
