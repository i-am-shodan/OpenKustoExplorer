namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Carries one bounded batch of graph export rows and its table-boundary metadata.
/// </summary>
public sealed class KustoGraphExportBatch
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphExportBatch"/> class.
    /// </summary>
    /// <param name="kind">Whether the batch contains nodes or edges.</param>
    /// <param name="rows">The ordered invariant string rows.</param>
    /// <param name="startsTable">Whether this batch starts the exported table.</param>
    /// <param name="endsTable">Whether this batch completes the exported table.</param>
    /// <param name="tableName">The generated result-table name when the batch starts a table.</param>
    /// <param name="columns">The ordered result columns when the batch starts a table.</param>
    public KustoGraphExportBatch(
        KustoGraphExportTableKind kind,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool startsTable = false,
        bool endsTable = false,
        string? tableName = null,
        IReadOnlyList<KustoResultColumn>? columns = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (startsTable)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
            ArgumentNullException.ThrowIfNull(columns);
        }
        else if (tableName is not null || columns is not null)
        {
            throw new ArgumentException("Only a table-start batch can carry table metadata.", nameof(tableName));
        }

        Kind = kind;
        Rows = rows;
        StartsTable = startsTable;
        EndsTable = endsTable;
        TableName = tableName;
        Columns = columns ?? [];
    }

    /// <summary>
    /// Gets the ordered result columns when this batch starts a table.
    /// </summary>
    public IReadOnlyList<KustoResultColumn> Columns { get; }

    /// <summary>
    /// Gets a value indicating whether this batch completes the exported table.
    /// </summary>
    public bool EndsTable { get; }

    /// <summary>
    /// Gets whether the batch contains nodes or edges.
    /// </summary>
    public KustoGraphExportTableKind Kind { get; }

    /// <summary>
    /// Gets the ordered invariant string rows.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    /// <summary>
    /// Gets a value indicating whether this batch starts the exported table.
    /// </summary>
    public bool StartsTable { get; }

    /// <summary>
    /// Gets the generated result-table name when this batch starts a table.
    /// </summary>
    public string? TableName { get; }
}
