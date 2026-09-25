using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Kusto.Execution;

namespace OpenKustoExplorer.Infrastructure.Tests.Execution;

/// <summary>
/// Verifies AOT-safe Kusto REST response materialization.
/// </summary>
public sealed class KustoRestResponseParserTests
{
    /// <summary>
    /// Verifies user result parsing, metadata filtering, and JSON scalar formatting.
    /// </summary>
    [Fact]
    public void ParseMaterializesUserTablesAndFiltersMetadata()
    {
        const string Response = """
            {
              "Tables": [
                {
                  "TableName": "PrimaryResult",
                  "Columns": [
                    { "ColumnName": "State", "DataType": "String", "ColumnType": "string" },
                    { "ColumnName": "Events", "DataType": "Int64", "ColumnType": "long" },
                    { "ColumnName": "Metadata", "DataType": "Object", "ColumnType": "dynamic" }
                  ],
                  "Rows": [["Texas", 42, {"source":"test"}], [null, 0, [1,2]]]
                },
                {
                  "TableName": "@ExtendedProperties",
                  "Columns": [
                    { "ColumnName": "TableId", "DataType": "Int32", "ColumnType": "int" },
                    { "ColumnName": "Key", "DataType": "String", "ColumnType": "string" },
                    { "ColumnName": "Value", "DataType": "String", "ColumnType": "string" }
                  ],
                  "Rows": [[0, "Visualization", "{\"Visualization\":\"timechart\",\"Title\":\"Traffic\",\"Series\":[\"Protocol\"],\"YColumns\":\"Events,Latency\",\"Legend\":\"visible\"}"]]
                }
              ]
            }
            """;

        KustoQueryResult result = KustoRestResponseParser.Parse(Response, TimeSpan.FromMilliseconds(25), 10);

        KustoResultTable table = Assert.Single(result.Tables);
        Assert.Equal("PrimaryResult", table.Name);
        Assert.Equal("dynamic", table.Columns[2].TypeName);
        Assert.Equal("42", table.Rows[0].Values[1]);
        Assert.Equal("{\"source\":\"test\"}", table.Rows[0].Values[2]);
        Assert.Empty(table.Rows[1].Values[0]);
        Assert.Equal("[1,2]", table.Rows[1].Values[2]);
        Assert.Equal("42", table.Rows[0].ResultValues[1].RawJson);
        Assert.Equal("{\"source\":\"test\"}", table.Rows[0].ResultValues[2].RawJson);
        Assert.True(table.Rows[1].ResultValues[0].IsNull);
        Assert.Equal("null", table.Rows[1].ResultValues[0].RawJson);
        Assert.Equal(KustoQueryResultCompleteness.Complete, result.Completeness);
        Assert.NotNull(result.Visualization);
        Assert.Equal(KustoVisualizationKind.TimeChart, result.Visualization.Kind);
        Assert.Equal("Traffic", result.Visualization.Title);
        Assert.Equal(["Protocol"], result.Visualization.SeriesColumns);
        Assert.Equal(["Events", "Latency"], result.Visualization.YColumns);
        Assert.True(result.Visualization.LegendVisible);
    }

    /// <summary>
    /// Verifies physical table names are resolved through the table of contents before materialization.
    /// </summary>
    [Fact]
    public void ParseUsesTableOfContentsToExcludeProtocolMetadata()
    {
        const string Response = """
            {
              "Tables": [
                {
                  "TableName": "Table_0",
                  "Columns": [{ "ColumnName": "State", "ColumnType": "string" }],
                  "Rows": [["Texas"]]
                },
                {
                  "TableName": "Table_1",
                  "Columns": [{ "ColumnName": "Value", "ColumnType": "dynamic" }],
                  "Rows": [[{"Visualization":"table"}]]
                },
                {
                  "TableName": "Table_2",
                  "Columns": [
                    { "ColumnName": "Severity", "ColumnType": "int" },
                    { "ColumnName": "StatusDescription", "ColumnType": "string" }
                  ],
                  "Rows": [[4, "Query completed"]]
                },
                {
                  "TableName": "Table_3",
                  "Columns": [
                    { "ColumnName": "Ordinal", "ColumnType": "long" },
                    { "ColumnName": "Kind", "ColumnType": "string" },
                    { "ColumnName": "Name", "ColumnType": "string" },
                    { "ColumnName": "Id", "ColumnType": "guid" },
                    { "ColumnName": "PrettyName", "ColumnType": "string" }
                  ],
                  "Rows": [
                    [0, "QueryResult", "PrimaryResult", "00000000-0000-0000-0000-000000000001", ""],
                    [1, "QueryProperties", "@ExtendedProperties", "00000000-0000-0000-0000-000000000002", ""],
                    [2, "QueryStatus", "QueryStatus", "00000000-0000-0000-0000-000000000000", ""]
                  ]
                }
              ]
            }
            """;

        KustoQueryResult result = KustoRestResponseParser.Parse(Response, TimeSpan.Zero, 10);

        KustoResultTable table = Assert.Single(result.Tables);
        Assert.Equal("PrimaryResult", table.Name);
        Assert.Equal("Texas", Assert.Single(table.Rows).Values[0]);
        Assert.NotNull(result.Visualization);

        string failedResponse = Response.Replace(
          "[4, \"Query completed\"]",
          "[2, \"Resource limit exceeded\"]",
          StringComparison.Ordinal);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
          () => KustoRestResponseParser.Parse(failedResponse, TimeSpan.Zero, 10));
        Assert.Contains("Resource limit exceeded", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the total row limit is enforced across user result tables.
    /// </summary>
    [Fact]
    public void ParseCapsRowsAcrossResultTables()
    {
        const string Response = """
            {
              "Tables": [
                {
                  "TableName": "First",
                  "Columns": [{ "ColumnName": "Value", "DataType": "Int32", "ColumnType": "int" }],
                  "Rows": [[1], [2]]
                },
                {
                  "TableName": "Second",
                  "Columns": [{ "ColumnName": "Value", "DataType": "Int32", "ColumnType": "int" }],
                  "Rows": [[3], [4]]
                }
              ]
            }
            """;

        KustoQueryResult result = KustoRestResponseParser.Parse(Response, TimeSpan.Zero, 3);

        Assert.Equal(2, result.Tables.Count);
        Assert.Equal(2, result.Tables[0].Rows.Count);
        Assert.Single(result.Tables[1].Rows);
        Assert.Equal("3", result.Tables[1].Rows[0].Values[0]);
        Assert.Equal(KustoQueryResultCompleteness.RecordLimitReached, result.Completeness);
    }

    /// <summary>
    /// Verifies that every documented Kusto.Explorer visualization name is retained.
    /// </summary>
    /// <param name="visualizationName">The server visualization name.</param>
    /// <param name="expectedKind">The application visualization kind.</param>
    [Theory]
    [InlineData("anomalychart", KustoVisualizationKind.AnomalyChart)]
    [InlineData("areachart", KustoVisualizationKind.AreaChart)]
    [InlineData("barchart", KustoVisualizationKind.BarChart)]
    [InlineData("card", KustoVisualizationKind.Card)]
    [InlineData("columnchart", KustoVisualizationKind.ColumnChart)]
    [InlineData("graph", KustoVisualizationKind.Graph)]
    [InlineData("ladderchart", KustoVisualizationKind.LadderChart)]
    [InlineData("linechart", KustoVisualizationKind.LineChart)]
    [InlineData("piechart", KustoVisualizationKind.PieChart)]
    [InlineData("pivotchart", KustoVisualizationKind.PivotChart)]
    [InlineData("scatterchart", KustoVisualizationKind.ScatterChart)]
    [InlineData("stackedareachart", KustoVisualizationKind.StackedAreaChart)]
    [InlineData("table", KustoVisualizationKind.Table)]
    [InlineData("timechart", KustoVisualizationKind.TimeChart)]
    [InlineData("timepivot", KustoVisualizationKind.TimePivot)]
    [InlineData("treemap", KustoVisualizationKind.TreeMap)]
    public void ParseSupportsEveryVisualizationKind(
        string visualizationName,
        KustoVisualizationKind expectedKind)
    {
        string response = $$"""
            {
              "Tables": [
                {
                  "TableName": "PrimaryResult",
                  "Columns": [{ "ColumnName": "Value", "ColumnType": "long" }],
                  "Rows": [[1]]
                },
                {
                  "TableName": "@ExtendedProperties",
                  "Columns": [
                    { "ColumnName": "Key", "ColumnType": "string" },
                    { "ColumnName": "Value", "ColumnType": "dynamic" }
                  ],
                  "Rows": [["Visualization", { "Visualization": "{{visualizationName}}" }]]
                }
              ]
            }
            """;

        KustoQueryResult result = KustoRestResponseParser.Parse(response, TimeSpan.Zero, 10);

        Assert.NotNull(result.Visualization);
        Assert.Equal(expectedKind, result.Visualization.Kind);
    }

    /// <summary>
    /// Verifies non-finite server axis bounds remain unspecified and safe for the gateway wire.
    /// </summary>
    /// <param name="bound">The non-finite server value.</param>
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void ParseIgnoresNonFiniteVisualizationBounds(string bound)
    {
        string response = $$"""
            {
              "Tables": [
                {
                  "TableName": "PrimaryResult",
                  "Columns": [{ "ColumnName": "Events", "ColumnType": "long" }],
                  "Rows": [[42]]
                },
                {
                  "TableName": "@ExtendedProperties",
                  "Columns": [
                    { "ColumnName": "Key", "ColumnType": "string" },
                    { "ColumnName": "Value", "ColumnType": "dynamic" }
                  ],
                  "Rows": [["Visualization", { "Visualization": "columnchart", "YMin": "{{bound}}" }]]
                }
              ]
            }
            """;

        KustoQueryResult result = KustoRestResponseParser.Parse(response, TimeSpan.Zero, 10);

        Assert.NotNull(result.Visualization);
        Assert.Null(result.Visualization.YMinimum);
    }

    /// <summary>
    /// Verifies that partial query failures are surfaced instead of appearing as successful data.
    /// </summary>
    [Fact]
    public void ParseRejectsFailedQueryStatus()
    {
        const string Response = """
            {
              "Tables": [{
                "TableName": "QueryStatus",
                "Columns": [
                  { "ColumnName": "Severity", "DataType": "Int32", "ColumnType": "int" },
                  { "ColumnName": "StatusDescription", "DataType": "String", "ColumnType": "string" }
                ],
                "Rows": [[2, "Resource limit exceeded"]]
              }]
            }
            """;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => KustoRestResponseParser.Parse(Response, TimeSpan.Zero, 10));

        Assert.Contains("Resource limit exceeded", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies failed requests surface Kusto's specific semantic diagnostic.
    /// </summary>
    [Fact]
    public void ParseErrorPrefersDetailedKustoMessage()
    {
        const string Response = """
            {
              "error": {
                "code": "BadRequest",
                "message": "Request is invalid and cannot be executed.",
                "@type": "Kusto.Data.Exceptions.SemanticException",
                "@message": "Semantic error: The graph export clause is not supported."
              }
            }
            """;

        string message = KustoRestResponseParser.ParseError(Response, "Kusto returned 400 Bad Request.");

        Assert.Equal("Semantic error: The graph export clause is not supported.", message);
    }
}
