using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Kusto.Execution;

/// <summary>
/// Consumes one token at a time from a Kusto graph export response and emits complete rows immediately.
/// </summary>
internal sealed class KustoGraphRestParserState : IDisposable
{
    private const int MaximumBatchRowCount = 256;
    private readonly List<KustoResultColumn> columns = [];
    private readonly List<KustoResultRow> edgeResultRows = [];
    private readonly int maximumResultRowCount;
    private readonly List<KustoGraphExportBatch> pendingBatches = [];
    private readonly KustoGraphQueryPlan plan;
    private readonly List<KustoResultValue> rowResultValues = [];
    private readonly List<string> rowValues = [];
    private readonly IKustoGraphExportSink sink;
    private string columnDataType = string.Empty;
    private int columnDepth;
    private string columnName = string.Empty;
    private string columnType = string.Empty;
    private bool edgeTableSeen;
    private KustoResultColumn[] edgeResultColumns = [];
    private string? failure;
    private bool inColumn;
    private bool inColumns;
    private bool inRow;
    private bool inRows;
    private bool inTable;
    private bool inTables;
    private JsonValueCapture? nestedValue;
    private bool nodeTableSeen;
    private string propertyName = string.Empty;
    private int rowDepth;
    private int rowsDepth;
    private int tableDepth;
    private string tableName = string.Empty;
    private KustoGraphExportTableKind? targetKind;
    private PendingBatch? currentBatch;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphRestParserState"/> class.
    /// </summary>
    /// <param name="plan">The validated graph export plan.</param>
    /// <param name="sink">The immediate row sink.</param>
    /// <param name="maximumResultRowCount">The maximum edge rows retained for the Results view.</param>
    internal KustoGraphRestParserState(
        KustoGraphQueryPlan plan,
        IKustoGraphExportSink sink,
        int maximumResultRowCount)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumResultRowCount);
        this.plan = plan;
        this.sink = sink;
        this.maximumResultRowCount = maximumResultRowCount;
    }

    /// <summary>
    /// Gets the number of emitted edge rows.
    /// </summary>
    public long EdgeCount { get; private set; }

    /// <summary>
    /// Gets the number of emitted node rows.
    /// </summary>
    public long NodeCount { get; private set; }

    /// <summary>
    /// Gets the bounded edge table materialized for the Results view.
    /// </summary>
    public KustoResultTable? ResultTable { get; private set; }

    /// <inheritdoc />
    void IDisposable.Dispose()
    {
        (nestedValue as IDisposable)?.Dispose();
        nestedValue = null;
    }

    /// <summary>
    /// Consumes the current JSON token.
    /// </summary>
    /// <param name="reader">The reader positioned on a complete token.</param>
    internal void Consume(ref Utf8JsonReader reader)
    {
        if (nestedValue is not null)
        {
            ConsumeNestedValue(ref reader);
        }
        else
        {
            ConsumeToken(ref reader);
        }
    }

    /// <summary>
    /// Delivers parsed table boundaries and rows in bounded batches.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels delivery.</param>
    /// <returns>A task that completes when pending batches have been accepted.</returns>
    internal async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        QueueCurrentBatch(false);

        foreach (KustoGraphExportBatch batch in pendingBatches)
        {
            await sink.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        pendingBatches.Clear();
    }

    /// <summary>
    /// Verifies required graph tables were seen and no partial query failure was reported.
    /// </summary>
    internal void ValidateComplete()
    {
        if (failure is not null)
        {
            throw new InvalidOperationException($"Kusto graph query failed: {failure}");
        }

        if (!nodeTableSeen || !edgeTableSeen)
        {
            throw new InvalidDataException("Kusto returned no complete node and edge graph export tables.");
        }

        ResultTable = new KustoResultTable("Graph edges", edgeResultColumns, edgeResultRows);
    }

    private static string FormatScalar(ref Utf8JsonReader reader)
    {
        string value = reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.ValueSpan),
            JsonTokenType.True => bool.TrueString.ToLowerInvariant(),
            JsonTokenType.False => bool.FalseString.ToLowerInvariant(),
            JsonTokenType.Null => string.Empty,
            _ => throw new InvalidDataException($"Unsupported Kusto graph cell token '{reader.TokenType}'."),
        };
        return value;
    }

    private static string GetRawScalar(ref Utf8JsonReader reader)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => EncodeJsonString(reader.GetString() ?? string.Empty),
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.ValueSpan),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => "null",
            _ => throw new InvalidDataException($"Unsupported Kusto graph cell token '{reader.TokenType}'."),
        };
    }

    private static string EncodeJsonString(string value)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStringValue(value);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void BeginRows()
    {
        targetKind = ClassifyTargetTable();

        if (targetKind == KustoGraphExportTableKind.Nodes)
        {
            if (nodeTableSeen)
            {
                throw new InvalidDataException($"Kusto returned duplicate graph node table '{plan.NodeTableName}'.");
            }

            nodeTableSeen = true;
        }
        else if (targetKind == KustoGraphExportTableKind.Edges)
        {
            if (edgeTableSeen)
            {
                throw new InvalidDataException($"Kusto returned duplicate graph edge table '{plan.EdgeTableName}'.");
            }

            edgeTableSeen = true;
            edgeResultColumns = CreateEdgeResultColumns();
        }

        if (targetKind is not null)
        {
            string logicalTableName = targetKind == KustoGraphExportTableKind.Nodes
                ? plan.NodeTableName
                : plan.EdgeTableName;
            currentBatch = new PendingBatch(
                targetKind.Value,
                true,
                logicalTableName,
                Array.AsReadOnly(columns.ToArray()));
        }
    }

    private KustoGraphExportTableKind? ClassifyTargetTable()
    {
        if (string.Equals(tableName, plan.NodeTableName, StringComparison.Ordinal))
        {
            return KustoGraphExportTableKind.Nodes;
        }

        if (string.Equals(tableName, plan.EdgeTableName, StringComparison.Ordinal))
        {
            return KustoGraphExportTableKind.Edges;
        }

        bool hasNodeHash = columns.Exists(column => string.Equals(
            column.Name,
            plan.NodeHashColumnName,
            StringComparison.Ordinal));
        bool hasSourceHash = columns.Exists(column => string.Equals(
            column.Name,
            plan.SourceHashColumnName,
            StringComparison.Ordinal));
        bool hasTargetHash = columns.Exists(column => string.Equals(
            column.Name,
            plan.TargetHashColumnName,
            StringComparison.Ordinal));

        if (hasSourceHash && hasTargetHash)
        {
            return KustoGraphExportTableKind.Edges;
        }

        return hasNodeHash ? KustoGraphExportTableKind.Nodes : null;
    }

    private KustoResultColumn[] CreateEdgeResultColumns()
    {
        bool hasSourceIdCollision = columns.Exists(column => string.Equals(
            column.Name,
            "_SId",
            StringComparison.OrdinalIgnoreCase)
            && !string.Equals(column.Name, plan.SourceHashColumnName, StringComparison.Ordinal));
        bool hasTargetIdCollision = columns.Exists(column => string.Equals(
            column.Name,
            "_TId",
            StringComparison.OrdinalIgnoreCase)
            && !string.Equals(column.Name, plan.TargetHashColumnName, StringComparison.Ordinal));
        return columns
            .Select(column => new KustoResultColumn(
                GetEdgeResultColumnName(column.Name, hasSourceIdCollision, hasTargetIdCollision),
                column.TypeName))
            .ToArray();
    }

    private string GetEdgeResultColumnName(
        string columnName,
        bool hasSourceIdCollision,
        bool hasTargetIdCollision)
    {
        string resultName = columnName;

        if (!hasSourceIdCollision
            && string.Equals(columnName, plan.SourceHashColumnName, StringComparison.Ordinal))
        {
            resultName = "_SId";
        }
        else if (!hasTargetIdCollision
            && string.Equals(columnName, plan.TargetHashColumnName, StringComparison.Ordinal))
        {
            resultName = "_TId";
        }

        return resultName;
    }

    private void CompleteColumn()
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new InvalidDataException($"Kusto graph table '{tableName}' contains an unnamed column.");
        }

        string typeName = string.IsNullOrWhiteSpace(columnType) ? columnDataType : columnType;
        columns.Add(new KustoResultColumn(columnName, typeName));
        columnName = string.Empty;
        columnType = string.Empty;
        columnDataType = string.Empty;
        inColumn = false;
    }

    private void CompleteRow()
    {
        if (targetKind is not null)
        {
            currentBatch ??= new PendingBatch(targetKind.Value, false, null, null);
            currentBatch.Rows.Add(Array.AsReadOnly(rowValues.ToArray()));
            if (currentBatch.Rows.Count >= MaximumBatchRowCount)
            {
                QueueCurrentBatch(false);
            }

            if (targetKind == KustoGraphExportTableKind.Nodes)
            {
                NodeCount++;
            }
            else
            {
                EdgeCount++;

                if (edgeResultRows.Count < maximumResultRowCount)
                {
                    edgeResultRows.Add(new KustoResultRow(rowResultValues));
                }
            }
        }
        else if (string.Equals(tableName, "QueryStatus", StringComparison.Ordinal))
        {
            CaptureQueryFailure();
        }

        rowValues.Clear();
        rowResultValues.Clear();
        inRow = false;
    }

    private void CompleteRows()
    {
        if (targetKind is not null)
        {
            currentBatch ??= new PendingBatch(targetKind.Value, false, null, null);
            QueueCurrentBatch(true);
        }

        targetKind = null;
        inRows = false;
    }

    private void QueueCurrentBatch(bool endsTable)
    {
        if (currentBatch is null)
        {
            return;
        }

        pendingBatches.Add(new KustoGraphExportBatch(
            currentBatch.Kind,
            currentBatch.Rows.AsReadOnly(),
            currentBatch.StartsTable,
            endsTable,
            currentBatch.TableName,
            currentBatch.Columns));
        currentBatch = null;
    }

    private void CompleteTable()
    {
        tableName = string.Empty;
        columns.Clear();
        propertyName = string.Empty;
        inTable = false;
    }

    private void ConsumeNestedValue(ref Utf8JsonReader reader)
    {
        nestedValue!.Append(ref reader);

        if (nestedValue.IsComplete)
        {
            string value = nestedValue.GetValue();
            rowValues.Add(value);
            rowResultValues.Add(new KustoResultValue(value, value, false));
            ((IDisposable)nestedValue).Dispose();
            nestedValue = null;
        }
    }

    private void ConsumeToken(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.PropertyName:
                propertyName = reader.GetString() ?? string.Empty;
                break;
            case JsonTokenType.StartObject:
                HandleStartObject(ref reader);
                break;
            case JsonTokenType.EndObject:
                HandleEndObject(ref reader);
                break;
            case JsonTokenType.StartArray:
                HandleStartArray(ref reader);
                break;
            case JsonTokenType.EndArray:
                HandleEndArray(ref reader);
                break;
            case JsonTokenType.String:
            case JsonTokenType.Number:
            case JsonTokenType.True:
            case JsonTokenType.False:
            case JsonTokenType.Null:
                HandleScalar(ref reader);
                break;
        }
    }

    private void CaptureQueryFailure()
    {
        int severityIndex = columns.FindIndex(column => string.Equals(
            column.Name,
            "Severity",
            StringComparison.OrdinalIgnoreCase));
        int descriptionIndex = columns.FindIndex(column => string.Equals(
            column.Name,
            "StatusDescription",
            StringComparison.OrdinalIgnoreCase));
        bool validIndexes = severityIndex >= 0
            && descriptionIndex >= 0
            && severityIndex < rowValues.Count
            && descriptionIndex < rowValues.Count;

        if (validIndexes
            && int.TryParse(rowValues[severityIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out int severity)
            && severity <= 2)
        {
            failure = rowValues[descriptionIndex];
        }
    }

    private void HandleEndArray(ref Utf8JsonReader reader)
    {
        if (inRow && reader.CurrentDepth == rowDepth)
        {
            CompleteRow();
        }
        else if (inRows && reader.CurrentDepth == rowsDepth)
        {
            CompleteRows();
        }
        else if (inColumns)
        {
            inColumns = false;
        }
        else if (inTables)
        {
            inTables = false;
        }
    }

    private void HandleEndObject(ref Utf8JsonReader reader)
    {
        if (inColumn && reader.CurrentDepth == columnDepth)
        {
            CompleteColumn();
        }
        else if (inTable && reader.CurrentDepth == tableDepth)
        {
            CompleteTable();
        }
    }

    private void HandleScalar(ref Utf8JsonReader reader)
    {
        string value = FormatScalar(ref reader);

        if (inRow)
        {
            rowValues.Add(value);
            rowResultValues.Add(new KustoResultValue(
                value,
                GetRawScalar(ref reader),
                reader.TokenType == JsonTokenType.Null));
        }
        else if (inColumn)
        {
            SetColumnProperty(value);
        }
        else if (inTable && string.Equals(propertyName, "TableName", StringComparison.Ordinal))
        {
            tableName = value;
        }

        propertyName = string.Empty;
    }

    private void HandleStartArray(ref Utf8JsonReader reader)
    {
        if (inRow)
        {
            nestedValue = new JsonValueCapture(ref reader);
        }
        else if (inRows)
        {
            inRow = true;
            rowDepth = reader.CurrentDepth;
            rowValues.Clear();
            rowResultValues.Clear();
        }
        else if (inTable && string.Equals(propertyName, "Columns", StringComparison.Ordinal))
        {
            inColumns = true;
            propertyName = string.Empty;
        }
        else if (inTable && string.Equals(propertyName, "Rows", StringComparison.Ordinal))
        {
            inRows = true;
            rowsDepth = reader.CurrentDepth;
            propertyName = string.Empty;
            BeginRows();
        }
        else if (!inTables && string.Equals(propertyName, "Tables", StringComparison.Ordinal))
        {
            inTables = true;
            propertyName = string.Empty;
        }
    }

    private void HandleStartObject(ref Utf8JsonReader reader)
    {
        if (inRow)
        {
            nestedValue = new JsonValueCapture(ref reader);
        }
        else if (inColumns && !inColumn)
        {
            inColumn = true;
            columnDepth = reader.CurrentDepth;
        }
        else if (inTables && !inTable)
        {
            inTable = true;
            tableDepth = reader.CurrentDepth;
            tableName = string.Empty;
            columns.Clear();
        }
    }

    private void SetColumnProperty(string value)
    {
        if (string.Equals(propertyName, "ColumnName", StringComparison.Ordinal))
        {
            columnName = value;
        }
        else if (string.Equals(propertyName, "ColumnType", StringComparison.Ordinal))
        {
            columnType = value;
        }
        else if (string.Equals(propertyName, "DataType", StringComparison.Ordinal))
        {
            columnDataType = value;
        }
    }

    private sealed class PendingBatch
    {
        internal PendingBatch(
            KustoGraphExportTableKind kind,
            bool startsTable,
            string? tableName,
            IReadOnlyList<KustoResultColumn>? columns)
        {
            Kind = kind;
            StartsTable = startsTable;
            TableName = tableName;
            Columns = columns;
        }

        internal IReadOnlyList<KustoResultColumn>? Columns { get; }

        internal KustoGraphExportTableKind Kind { get; }

        internal List<IReadOnlyList<string>> Rows { get; } = [];

        internal bool StartsTable { get; }

        internal string? TableName { get; }
    }
}
