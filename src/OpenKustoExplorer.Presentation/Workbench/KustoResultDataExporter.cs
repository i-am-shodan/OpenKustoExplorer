using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Serializes materialized Kusto results into clipboard, file, KQL, and filter representations.
/// </summary>
public static class KustoResultDataExporter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    /// <summary>
    /// Creates a file export for a complete result table.
    /// </summary>
    /// <param name="table">The materialized result table.</param>
    /// <param name="format">The requested export format.</param>
    /// <returns>The generated export file.</returns>
    public static KustoResultExportFile CreateFile(
        KustoResultTable table,
        KustoResultExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(table);

        string baseName = SanitizeFileName(table.Name);
        KustoResultExportFile export = format switch
        {
            KustoResultExportFormat.Csv => new KustoResultExportFile(
                $"{baseName}.csv",
                "text/csv",
                Utf8WithoutBom.GetBytes(CreateDelimitedText(table, table.Rows, ','))),
            KustoResultExportFormat.Excel => new KustoResultExportFile(
                $"{baseName}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                CreateExcelWorkbook(table)),
            KustoResultExportFormat.Json => new KustoResultExportFile(
                $"{baseName}.json",
                "application/json",
                CreateJson(table)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported result export format."),
        };

        return export;
    }

    /// <summary>
    /// Creates tab-separated clipboard text with a header row.
    /// </summary>
    /// <param name="table">The materialized result table.</param>
    /// <param name="rows">The rows to include.</param>
    /// <returns>The tab-separated clipboard text.</returns>
    public static string CreateClipboardText(
        KustoResultTable table,
        IReadOnlyList<KustoResultRow> rows)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(rows);
        return CreateDelimitedText(table, rows, '\t');
    }

    /// <summary>
    /// Creates a runnable KQL datatable expression for selected result rows.
    /// </summary>
    /// <param name="table">The materialized result table.</param>
    /// <param name="rows">The rows to include.</param>
    /// <returns>The KQL datatable expression.</returns>
    public static string CreateKqlDatatable(
        KustoResultTable table,
        IReadOnlyList<KustoResultRow> rows)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(rows);

        StringBuilder builder = new();
        builder.Append("datatable(");
        builder.AppendJoin(
            ", ",
            table.Columns.Select(column => $"{EscapeIdentifier(column.Name)}:{GetKustoType(column.TypeName)}"));
        builder.AppendLine(") [");

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            KustoResultRow row = rows[rowIndex];
            builder.Append("    ");
            builder.AppendJoin(
                ", ",
                row.Values.Select((value, columnIndex) => FormatKustoLiteral(
                    value,
                    table.Columns[columnIndex].TypeName)));
            builder.AppendLine(rowIndex == rows.Count - 1 ? string.Empty : ",");
        }

        builder.Append(']');
        return builder.ToString();
    }

    /// <summary>
    /// Creates a KQL predicate for one cell or a complete row.
    /// </summary>
    /// <param name="table">The materialized result table.</param>
    /// <param name="row">The source row.</param>
    /// <param name="columnIndex">The source column index.</param>
    /// <param name="includeWholeRow">Whether every row value participates.</param>
    /// <returns>The safe KQL predicate.</returns>
    public static string CreateFilterPredicate(
        KustoResultTable table,
        KustoResultRow row,
        int columnIndex,
        bool includeWholeRow)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(columnIndex, table.Columns.Count);

        IEnumerable<int> columnIndexes = includeWholeRow
            ? Enumerable.Range(0, table.Columns.Count)
            : [columnIndex];
        string predicate = string.Join(
            " and ",
            columnIndexes.Select(index => CreateColumnPredicate(
                table.Columns[index],
                row.Values[index])));

        return predicate;
    }

    private static string CreateDelimitedText(
        KustoResultTable table,
        IReadOnlyList<KustoResultRow> rows,
        char delimiter)
    {
        StringBuilder builder = new();
        builder.AppendJoin(delimiter, table.Columns.Select(column => EscapeDelimited(column.Name, delimiter)));
        builder.AppendLine();

        foreach (KustoResultRow row in rows)
        {
            builder.AppendJoin(delimiter, row.Values.Select(value => EscapeDelimited(value, delimiter)));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string EscapeDelimited(string value, char delimiter)
    {
        bool requiresQuotes = value.Contains(delimiter, StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal);
        return requiresQuotes ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }

    private static byte[] CreateJson(KustoResultTable table)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();

            foreach (KustoResultRow row in table.Rows)
            {
                writer.WriteStartObject();

                for (int index = 0; index < table.Columns.Count; index++)
                {
                    writer.WriteString(table.Columns[index].Name, row.Values[index]);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return stream.ToArray();
    }

    private static byte[] CreateExcelWorkbook(KustoResultTable table)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteXmlEntry(archive, "[Content_Types].xml", WriteContentTypes);
            WriteXmlEntry(archive, "_rels/.rels", WritePackageRelationships);
            WriteXmlEntry(archive, "xl/workbook.xml", WriteWorkbook);
            WriteXmlEntry(archive, "xl/_rels/workbook.xml.rels", WriteWorkbookRelationships);
            WriteXmlEntry(archive, "xl/styles.xml", WriteStyles);
            WriteXmlEntry(archive, "xl/worksheets/sheet1.xml", writer => WriteWorksheet(writer, table));
        }

        return stream.ToArray();
    }

    private static void WriteXmlEntry(
        ZipArchive archive,
        string entryName,
        Action<XmlWriter> writeAction)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        using XmlWriter writer = XmlWriter.Create(
            entryStream,
            new XmlWriterSettings { Encoding = Utf8WithoutBom, CloseOutput = false });
        writeAction(writer);
    }

    private static void WriteContentTypes(XmlWriter writer)
    {
        const string Namespace = "http://schemas.openxmlformats.org/package/2006/content-types";
        writer.WriteStartDocument();
        writer.WriteStartElement("Types", Namespace);
        WriteContentType(writer, "Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml");
        WriteContentType(writer, "Default", "Extension", "xml", "application/xml");
        WriteContentType(writer, "Override", "PartName", "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        WriteContentType(writer, "Override", "PartName", "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        WriteContentType(writer, "Override", "PartName", "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        writer.WriteEndElement();
    }

    private static void WriteContentType(
        XmlWriter writer,
        string elementName,
        string keyName,
        string keyValue,
        string contentType)
    {
        writer.WriteStartElement(elementName);
        writer.WriteAttributeString(keyName, keyValue);
        writer.WriteAttributeString("ContentType", contentType);
        writer.WriteEndElement();
    }

    private static void WritePackageRelationships(XmlWriter writer)
    {
        const string Namespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", Namespace);
        WriteRelationship(
            writer,
            "rId1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
            "xl/workbook.xml");
        writer.WriteEndElement();
    }

    private static void WriteWorkbook(XmlWriter writer)
    {
        const string Namespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        writer.WriteStartDocument();
        writer.WriteStartElement("workbook", Namespace);
        writer.WriteAttributeString("xmlns", "r", null, RelationshipsNamespace);
        writer.WriteStartElement("sheets");
        writer.WriteStartElement("sheet");
        writer.WriteAttributeString("name", "Results");
        writer.WriteAttributeString("sheetId", "1");
        writer.WriteAttributeString("r", "id", RelationshipsNamespace, "rId1");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorkbookRelationships(XmlWriter writer)
    {
        const string Namespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", Namespace);
        WriteRelationship(
            writer,
            "rId1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
            "worksheets/sheet1.xml");
        WriteRelationship(
            writer,
            "rId2",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles",
            "styles.xml");
        writer.WriteEndElement();
    }

    private static void WriteRelationship(XmlWriter writer, string id, string type, string target)
    {
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", id);
        writer.WriteAttributeString("Type", type);
        writer.WriteAttributeString("Target", target);
        writer.WriteEndElement();
    }

    private static void WriteStyles(XmlWriter writer)
    {
        const string Namespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        writer.WriteStartDocument();
        writer.WriteStartElement("styleSheet", Namespace);
        writer.WriteStartElement("fonts");
        writer.WriteAttributeString("count", "2");
        writer.WriteStartElement("font");
        writer.WriteStartElement("sz");
        writer.WriteAttributeString("val", "11");
        writer.WriteEndElement();
        writer.WriteStartElement("name");
        writer.WriteAttributeString("val", "Calibri");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("font");
        writer.WriteStartElement("b");
        writer.WriteEndElement();
        writer.WriteStartElement("sz");
        writer.WriteAttributeString("val", "11");
        writer.WriteEndElement();
        writer.WriteStartElement("name");
        writer.WriteAttributeString("val", "Calibri");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("fills");
        writer.WriteAttributeString("count", "2");
        writer.WriteStartElement("fill");
        writer.WriteStartElement("patternFill");
        writer.WriteAttributeString("patternType", "none");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("fill");
        writer.WriteStartElement("patternFill");
        writer.WriteAttributeString("patternType", "gray125");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("borders");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("border");
        writer.WriteStartElement("left");
        writer.WriteEndElement();
        writer.WriteStartElement("right");
        writer.WriteEndElement();
        writer.WriteStartElement("top");
        writer.WriteEndElement();
        writer.WriteStartElement("bottom");
        writer.WriteEndElement();
        writer.WriteStartElement("diagonal");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("cellStyleXfs");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("xf");
        writer.WriteAttributeString("numFmtId", "0");
        writer.WriteAttributeString("fontId", "0");
        writer.WriteAttributeString("fillId", "0");
        writer.WriteAttributeString("borderId", "0");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("cellXfs");
        writer.WriteAttributeString("count", "2");
        WriteCellStyle(writer, 0);
        WriteCellStyle(writer, 1);
        writer.WriteEndElement();
        writer.WriteStartElement("cellStyles");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("cellStyle");
        writer.WriteAttributeString("name", "Normal");
        writer.WriteAttributeString("xfId", "0");
        writer.WriteAttributeString("builtinId", "0");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteCellStyle(XmlWriter writer, int fontId)
    {
        writer.WriteStartElement("xf");
        writer.WriteAttributeString("numFmtId", "0");
        writer.WriteAttributeString("fontId", fontId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fillId", "0");
        writer.WriteAttributeString("borderId", "0");
        writer.WriteAttributeString("xfId", "0");
        writer.WriteEndElement();
    }

    private static void WriteWorksheet(XmlWriter writer, KustoResultTable table)
    {
        const string Namespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", Namespace);
        writer.WriteStartElement("sheetData");
        WriteWorksheetRow(writer, 1, table.Columns.Select(column => column.Name), header: true);

        for (int index = 0; index < table.Rows.Count; index++)
        {
            WriteWorksheetRow(writer, index + 2, table.Rows[index].Values, header: false);
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorksheetRow(
        XmlWriter writer,
        int rowNumber,
        IEnumerable<string> values,
        bool header)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
        int columnIndex = 0;

        foreach (string value in values)
        {
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", $"{GetColumnName(columnIndex)}{rowNumber}");
            writer.WriteAttributeString("t", "inlineStr");
            if (header)
            {
                writer.WriteAttributeString("s", "1");
            }

            writer.WriteStartElement("is");
            writer.WriteElementString("t", value);
            writer.WriteEndElement();
            writer.WriteEndElement();
            columnIndex++;
        }

        writer.WriteEndElement();
    }

    private static string GetColumnName(int zeroBasedIndex)
    {
        StringBuilder name = new();
        int value = zeroBasedIndex + 1;

        while (value > 0)
        {
            value--;
            name.Insert(0, (char)('A' + (value % 26)));
            value /= 26;
        }

        return name.ToString();
    }

    private static string CreateColumnPredicate(KustoResultColumn column, string value)
    {
        string identifier = EscapeIdentifier(column.Name);
        return string.IsNullOrEmpty(value)
            ? $"isempty({identifier})"
            : $"{identifier} == {FormatKustoLiteral(value, column.TypeName)}";
    }

    private static string EscapeIdentifier(string name)
    {
        return $"['{name.Replace("'", "''", StringComparison.Ordinal)}']";
    }

    private static string GetKustoType(string typeName)
    {
        string normalizedType = typeName.ToLowerInvariant();
        string kustoType = normalizedType switch
        {
            _ when normalizedType.Contains("bool", StringComparison.Ordinal) => "bool",
            _ when normalizedType.Contains("datetime", StringComparison.Ordinal) => "datetime",
            _ when normalizedType.Contains("decimal", StringComparison.Ordinal) => "decimal",
            _ when normalizedType.Contains("guid", StringComparison.Ordinal) => "guid",
            _ when normalizedType.Contains("int32", StringComparison.Ordinal) || normalizedType == "int" => "int",
            _ when normalizedType.Contains("int64", StringComparison.Ordinal) || normalizedType == "long" => "long",
            _ when normalizedType.Contains("double", StringComparison.Ordinal) || normalizedType.Contains("real", StringComparison.Ordinal) => "real",
            _ when normalizedType.Contains("timespan", StringComparison.Ordinal) => "timespan",
            _ when normalizedType.Contains("dynamic", StringComparison.Ordinal) || normalizedType.Contains("object", StringComparison.Ordinal) => "dynamic",
            _ => "string",
        };

        return kustoType;
    }

    private static string FormatKustoLiteral(string value, string typeName)
    {
        string kustoType = GetKustoType(typeName);

        if (string.IsNullOrEmpty(value))
        {
            return kustoType == "string" ? "''" : $"{kustoType}(null)";
        }

        string escapedValue = value.Replace("'", "''", StringComparison.Ordinal);
        return kustoType switch
        {
            "bool" => FormatBooleanLiteral(value, escapedValue),
            "int" or "long" => FormatIntegerLiteral(value, kustoType),
            "real" or "decimal" => FormatNumericLiteral(value, kustoType),
            "datetime" => $"datetime('{escapedValue}')",
            "timespan" => $"time('{escapedValue}')",
            "guid" => $"guid('{escapedValue}')",
            "dynamic" => FormatDynamicLiteral(value, escapedValue),
            _ => $"'{escapedValue}'",
        };
    }

    private static string FormatBooleanLiteral(string value, string escapedValue)
    {
        if (!bool.TryParse(value, out bool parsed))
        {
            return $"'{escapedValue}'";
        }

        return parsed ? "true" : "false";
    }

    private static string FormatIntegerLiteral(string value, string kustoType)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            ? value
            : $"{kustoType}(null)";
    }

    private static string FormatNumericLiteral(string value, string kustoType)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            && double.IsFinite(parsed)
                ? value
                : $"{kustoType}(null)";
    }

    private static string FormatDynamicLiteral(string value, string escapedValue)
    {
        return IsWellFormedJson(value) ? $"dynamic({value})" : $"dynamic('{escapedValue}')";
    }

    private static bool IsWellFormedJson(string value)
    {
        try
        {
            using (JsonDocument.Parse(value))
            {
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string SanitizeFileName(string value)
    {
        HashSet<char> invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        string sanitized = new(value.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "kusto-results" : sanitized;
    }
}
