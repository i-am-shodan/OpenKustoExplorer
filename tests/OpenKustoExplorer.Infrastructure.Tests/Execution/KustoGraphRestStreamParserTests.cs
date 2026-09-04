using System.Text;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Infrastructure.Execution;

namespace OpenKustoExplorer.Infrastructure.Tests.Execution;

/// <summary>
/// Verifies bounded-memory Kusto graph export parsing across arbitrary stream chunks.
/// </summary>
public sealed class KustoGraphRestStreamParserTests
{
    /// <summary>
    /// Verifies node and edge rows, including dynamic cells, stream across tiny read boundaries.
    /// </summary>
    /// <returns>A task that completes when the response is parsed.</returns>
    [Fact]
    public async Task ParseStreamsGraphRowsAcrossChunkBoundaries()
    {
        const string Response = """
            {
              "Tables": [
                {
                  "TableName": "nodes_export",
                  "Columns": [
                    { "ColumnName": "node_hash", "ColumnType": "long" },
                    { "ColumnName": "name", "ColumnType": "string" },
                    { "ColumnName": "properties", "ColumnType": "dynamic" }
                  ],
                  "Rows": [
                    [1, "Alice", {"roles":["Admin"],"enabled":true}],
                    [2, "Server 1", null]
                  ]
                },
                {
                  "TableName": "edges_export",
                  "Columns": [
                    { "ColumnName": "source_hash", "ColumnType": "long" },
                    { "ColumnName": "target_hash", "ColumnType": "long" },
                    { "ColumnName": "relationship", "ColumnType": "string" },
                    { "ColumnName": "properties", "ColumnType": "dynamic" }
                  ],
                  "Rows": [[1, 2, "AuthenticatedTo", [1,2]]]
                },
                {
                  "TableName": "QueryStatus",
                  "Columns": [
                    { "ColumnName": "Severity", "ColumnType": "int" },
                    { "ColumnName": "StatusDescription", "ColumnType": "string" }
                  ],
                  "Rows": [[4, "Completed"]]
                }
              ]
            }
            """;
        CollectingGraphSink sink = new();
        using ChunkedMemoryStream stream = new(Encoding.UTF8.GetBytes(Response), 7);

        KustoGraphExportSummary summary = await KustoGraphRestStreamParser.ParseAsync(
            stream,
            CreatePlan(),
            sink,
            TimeSpan.FromMilliseconds(25));

        Assert.Equal(2, summary.NodeCount);
        Assert.Equal(1, summary.EdgeCount);
        Assert.Equal(TimeSpan.FromMilliseconds(25), summary.Duration);
        KustoResultTable resultTable = Assert.IsType<KustoResultTable>(summary.ResultTable);
        Assert.Equal("Graph edges", resultTable.Name);
        Assert.Equal(
          ["_SId", "_TId", "relationship", "properties"],
          resultTable.Columns.Select(column => column.Name));
        Assert.Equal(["1", "2", "AuthenticatedTo", "[1,2]"], Assert.Single(resultTable.Rows).Values);
        Assert.Equal(["node_hash", "name", "properties"], sink.NodeColumns.Select(column => column.Name));
        Assert.Equal("{\"roles\":[\"Admin\"],\"enabled\":true}", sink.NodeRows[0][2]);
        Assert.Empty(sink.NodeRows[1][2]);
        Assert.Equal("[1,2]", Assert.Single(sink.EdgeRows)[3]);
        Assert.True(sink.NodesCompleted);
        Assert.True(sink.EdgesCompleted);
    }

    /// <summary>
    /// Verifies a partial Kusto failure invalidates rows already sent to staged storage.
    /// </summary>
    /// <returns>A task that completes when the failure is detected.</returns>
    [Fact]
    public async Task ParseRejectsPartialQueryFailureAfterGraphRows()
    {
        const string Response = """
            {
              "Tables": [
                {
                  "TableName": "nodes_export",
                  "Columns": [{ "ColumnName": "node_hash", "ColumnType": "long" }],
                  "Rows": [[1]]
                },
                {
                  "TableName": "edges_export",
                  "Columns": [
                    { "ColumnName": "source_hash", "ColumnType": "long" },
                    { "ColumnName": "target_hash", "ColumnType": "long" }
                  ],
                  "Rows": []
                },
                {
                  "TableName": "QueryStatus",
                  "Columns": [
                    { "ColumnName": "Severity", "ColumnType": "int" },
                    { "ColumnName": "StatusDescription", "ColumnType": "string" }
                  ],
                  "Rows": [[2, "Resource limit exceeded"]]
                }
              ]
            }
            """;
        CollectingGraphSink sink = new();
        using ChunkedMemoryStream stream = new(Encoding.UTF8.GetBytes(Response), 11);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            KustoGraphRestStreamParser.ParseAsync(stream, CreatePlan(), sink, TimeSpan.Zero));

        Assert.Contains("Resource limit exceeded", exception.Message, StringComparison.Ordinal);
        Assert.Single(sink.NodeRows);
    }

    /// <summary>
    /// Verifies both generated result tables are required before staged data can commit.
    /// </summary>
    /// <returns>A task that completes when missing-table validation runs.</returns>
    [Fact]
    public async Task ParseRejectsMissingEdgeTable()
    {
        const string Response = """
            {
              "Tables": [{
                "TableName": "nodes_export",
                "Columns": [{ "ColumnName": "node_hash", "ColumnType": "long" }],
                "Rows": [[1]]
              }]
            }
            """;
        CollectingGraphSink sink = new();
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(Response));

        await Assert.ThrowsAsync<InvalidDataException>(() => KustoGraphRestStreamParser.ParseAsync(
            stream,
            CreatePlan(),
            sink,
            TimeSpan.Zero));
    }

    /// <summary>
    /// Verifies v1 REST primary-result names are classified by generated graph hash columns.
    /// </summary>
    /// <returns>A task that completes when both generic result tables are parsed.</returns>
    [Fact]
    public async Task ParseRecognizesGenericResultTableNames()
    {
        const string Response = """
            {
              "Tables": [
                {
                  "TableName": "PrimaryResult",
                  "Columns": [
                    { "ColumnName": "node_hash", "ColumnType": "long" },
                    { "ColumnName": "name", "ColumnType": "string" }
                  ],
                  "Rows": [[1, "Alice"], [2, "Server 1"]]
                },
                {
                  "TableName": "PrimaryResult",
                  "Columns": [
                    { "ColumnName": "source_hash", "ColumnType": "long" },
                    { "ColumnName": "target_hash", "ColumnType": "long" },
                    { "ColumnName": "relationship", "ColumnType": "string" }
                  ],
                  "Rows": [[1, 2, "AuthenticatedTo"]]
                }
              ]
            }
            """;
        CollectingGraphSink sink = new();
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(Response));

        KustoGraphExportSummary summary = await KustoGraphRestStreamParser.ParseAsync(
            stream,
            CreatePlan(),
            sink,
            TimeSpan.Zero);

        Assert.Equal(2, summary.NodeCount);
        Assert.Equal(1, summary.EdgeCount);
        Assert.Equal("nodes_export", sink.NodeTableName);
        Assert.Equal("edges_export", sink.EdgeTableName);
    }

    /// <summary>
    /// Verifies the Results copy is bounded without dropping rows from staged graph ingestion.
    /// </summary>
    /// <returns>A task that completes when all edge rows have streamed.</returns>
    [Fact]
    public async Task ParseBoundsMaterializedResultsWithoutBoundingIngestion()
    {
        const string Response = """
            {
              "Tables": [
                {
                  "TableName": "nodes_export",
                  "Columns": [{ "ColumnName": "node_hash", "ColumnType": "long" }],
                  "Rows": [[1], [2]]
                },
                {
                  "TableName": "edges_export",
                  "Columns": [
                    { "ColumnName": "source_hash", "ColumnType": "long" },
                    { "ColumnName": "target_hash", "ColumnType": "long" }
                  ],
                  "Rows": [[1, 2], [2, 1]]
                }
              ]
            }
            """;
        CollectingGraphSink sink = new();
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(Response));

        KustoGraphExportSummary summary = await KustoGraphRestStreamParser.ParseAsync(
            stream,
            CreatePlan(),
            sink,
            TimeSpan.Zero,
            maximumResultRowCount: 1);

        Assert.Equal(2, summary.EdgeCount);
        Assert.Equal(2, sink.EdgeRows.Count);
        Assert.Single(Assert.IsType<KustoResultTable>(summary.ResultTable).Rows);
    }

    private static KustoGraphQueryPlan CreatePlan()
    {
        return new KustoGraphQueryPlan(
            new KustoQuerySelection("graph(\"SecurityGraph\")", 0, 22),
            KustoGraphSourceKind.GraphFunction,
            "export query",
            "nodes_export",
            "edges_export",
            "node_hash",
            "source_hash",
            "target_hash",
            null,
            null);
    }

    private sealed class ChunkedMemoryStream : MemoryStream
    {
        private readonly int maximumReadSize;

        internal ChunkedMemoryStream(byte[] buffer, int maximumReadSize)
            : base(buffer)
        {
            this.maximumReadSize = maximumReadSize;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumReadSize)], cancellationToken);
        }
    }

    private sealed class CollectingGraphSink : IKustoGraphExportSink
    {
        internal IReadOnlyList<KustoResultColumn> EdgeColumns { get; private set; } = [];

        internal List<IReadOnlyList<string>> EdgeRows { get; } = [];

        internal string EdgeTableName { get; private set; } = string.Empty;

        internal bool EdgesCompleted { get; private set; }

        internal IReadOnlyList<KustoResultColumn> NodeColumns { get; private set; } = [];

        internal List<IReadOnlyList<string>> NodeRows { get; } = [];

        internal string NodeTableName { get; private set; } = string.Empty;

        internal bool NodesCompleted { get; private set; }

        public void BeginTable(
            KustoGraphExportTableKind kind,
            string tableName,
            IReadOnlyList<KustoResultColumn> columns)
        {
            if (kind == KustoGraphExportTableKind.Nodes)
            {
                NodeColumns = columns;
                NodeTableName = tableName;
            }
            else
            {
                EdgeColumns = columns;
                EdgeTableName = tableName;
            }
        }

        public void EndTable(KustoGraphExportTableKind kind)
        {
            if (kind == KustoGraphExportTableKind.Nodes)
            {
                NodesCompleted = true;
            }
            else
            {
                EdgesCompleted = true;
            }
        }

        public void WriteRow(KustoGraphExportTableKind kind, IReadOnlyList<string> values)
        {
            if (kind == KustoGraphExportTableKind.Nodes)
            {
                NodeRows.Add(values);
            }
            else
            {
                EdgeRows.Add(values);
            }
        }
    }
}
