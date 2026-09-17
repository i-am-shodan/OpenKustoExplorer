using System.IO.Compression;
using System.Text;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Workbench;

/// <summary>
/// Verifies result serialization and safe KQL generation.
/// </summary>
public sealed class KustoResultDataExporterTests
{
    /// <summary>
    /// Verifies clipboard, CSV, and JSON exports preserve headers and escaped values.
    /// </summary>
    [Fact]
    public void TextExportsPreserveAndEscapeValues()
    {
        KustoResultTable table = CreateTable();

        string clipboard = KustoResultDataExporter.CreateClipboardText(table, table.Rows);
        KustoResultExportFile csv = KustoResultDataExporter.CreateFile(table, KustoResultExportFormat.Csv);
        KustoResultExportFile json = KustoResultDataExporter.CreateFile(table, KustoResultExportFormat.Json);

        Assert.Contains("State\tEvents", clipboard, StringComparison.Ordinal);
        Assert.Contains("Texas, north\t42", clipboard, StringComparison.Ordinal);
        Assert.Contains("\"Texas, north\",42", Encoding.UTF8.GetString(csv.Content), StringComparison.Ordinal);
        Assert.Contains("\"State\": \"Texas, north\"", Encoding.UTF8.GetString(json.Content), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Excel export creates the required Open XML package parts.
    /// </summary>
    [Fact]
    public void ExcelExportCreatesWorkbookPackage()
    {
        KustoResultExportFile excel = KustoResultDataExporter.CreateFile(
            CreateTable(),
            KustoResultExportFormat.Excel);
        using MemoryStream stream = new(excel.Content);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
        Assert.NotNull(archive.GetEntry("xl/styles.xml"));
    }

    /// <summary>
    /// Verifies datatable and filter KQL use escaped identifiers and typed literals.
    /// </summary>
    [Fact]
    public void KqlExportsUseTypedSafeLiterals()
    {
        KustoResultTable table = CreateTable();

        string datatable = KustoResultDataExporter.CreateKqlDatatable(table, table.Rows);
        string predicate = KustoResultDataExporter.CreateFilterPredicate(
            table,
            table.Rows[0],
            0,
            includeWholeRow: false);

        Assert.Contains("datatable(['State']:string, ['Events']:long)", datatable, StringComparison.Ordinal);
        Assert.Contains("'Texas, north', 42", datatable, StringComparison.Ordinal);
        Assert.Equal("['State'] == 'Texas, north'", predicate);
    }

    /// <summary>
    /// Verifies selected values are deduplicated and combined with an OR expression.
    /// </summary>
    [Fact]
    public void MultipleValuesCreateOrFilterPredicate()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("State", "string")],
            [
                new KustoResultRow(["Texas"]),
                new KustoResultRow(["Ohio"]),
                new KustoResultRow(["Texas"]),
            ]);

        string predicate = KustoResultDataExporter.CreateFilterPredicate(table, table.Rows, 0);

        Assert.Equal("(['State'] == 'Texas' or ['State'] == 'Ohio')", predicate);
    }

    /// <summary>
    /// Verifies missing string values use Kusto's valid empty string literal rather than an unsupported typed null.
    /// </summary>
    [Fact]
    public void KqlDatatableUsesEmptyLiteralForMissingStrings()
    {
        KustoResultTable table = new(
            "Result 1",
            [
                new KustoResultColumn("IPAddress", "string"),
                new KustoResultColumn("OperatingSystem", "string"),
                new KustoResultColumn("WhoisOrganisation", "string"),
                new KustoResultColumn("RiskScore", "long"),
            ],
            [new KustoResultRow(["139.19.117.129", "Linux", string.Empty, string.Empty])]);

        string datatable = KustoResultDataExporter.CreateKqlDatatable(table, table.Rows);

        Assert.Contains("'139.19.117.129', 'Linux', '', long(null)", datatable, StringComparison.Ordinal);
        Assert.DoesNotContain("string(null)", datatable, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies every file format honors row projection order and KQL emits a runnable script file.
    /// </summary>
    [Fact]
    public void FileExportsUseRequestedRowsInRequestedOrder()
    {
        KustoResultTable table = CreateTable();
        IReadOnlyList<KustoResultRow> rows = [table.Rows[1]];

        KustoResultExportFile csv = KustoResultDataExporter.CreateFile(
            table,
            rows,
            KustoResultExportFormat.Csv);
        KustoResultExportFile json = KustoResultDataExporter.CreateFile(
            table,
            rows,
            KustoResultExportFormat.Json);
        KustoResultExportFile kql = KustoResultDataExporter.CreateFile(
            table,
            rows,
            KustoResultExportFormat.KqlScript);

        Assert.DoesNotContain("Texas", Encoding.UTF8.GetString(csv.Content), StringComparison.Ordinal);
        Assert.DoesNotContain("Texas", Encoding.UTF8.GetString(json.Content), StringComparison.Ordinal);
        Assert.DoesNotContain("Texas", Encoding.UTF8.GetString(kql.Content), StringComparison.Ordinal);
        Assert.EndsWith(".kql", kql.SuggestedFileName, StringComparison.Ordinal);
        Assert.Contains("datatable(", Encoding.UTF8.GetString(kql.Content), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies JSON exports preserve native server tokens and null values.
    /// </summary>
    [Fact]
    public void JsonExportPreservesNativeTokens()
    {
        KustoResultTable table = new(
            "Typed",
            [
                new KustoResultColumn("Count", "long"),
                new KustoResultColumn("Enabled", "bool"),
                new KustoResultColumn("Missing", "string"),
            ],
            [
                new KustoResultRow(
                [
                    new KustoResultValue("42", "42", false),
                    new KustoResultValue("true", "true", false),
                    new KustoResultValue(string.Empty, "null", true),
                ]),
            ]);

        string json = Encoding.UTF8.GetString(KustoResultDataExporter.CreateFile(
            table,
            KustoResultExportFormat.Json).Content);

        Assert.Contains("\"Count\": 42", json, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"Missing\": null", json, StringComparison.Ordinal);
    }

    private static KustoResultTable CreateTable()
    {
        return new KustoResultTable(
            "Result 1",
            [new KustoResultColumn("State", "string"), new KustoResultColumn("Events", "long")],
            [new KustoResultRow(["Texas, north", "42"]), new KustoResultRow(["Ohio", "9"])]);
    }
}
