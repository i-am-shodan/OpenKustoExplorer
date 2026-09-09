namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Locates one persisted result cell.
/// </summary>
public sealed class KustoRecordedValueCoordinate
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedValueCoordinate"/> class.
    /// </summary>
    /// <param name="executionId">The recorded execution identifier.</param>
    /// <param name="tableOrdinal">The zero-based result-table position.</param>
    /// <param name="rowOrdinal">The zero-based result-row position.</param>
    /// <param name="columnOrdinal">The zero-based result-column position.</param>
    public KustoRecordedValueCoordinate(
        Guid executionId,
        int tableOrdinal,
        int rowOrdinal,
        int columnOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(tableOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(rowOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(columnOrdinal);
        ExecutionId = executionId;
        TableOrdinal = tableOrdinal;
        RowOrdinal = rowOrdinal;
        ColumnOrdinal = columnOrdinal;
    }

    /// <summary>Gets the recorded execution identifier.</summary>
    public Guid ExecutionId { get; }

    /// <summary>Gets the zero-based result-table position.</summary>
    public int TableOrdinal { get; }

    /// <summary>Gets the zero-based result-row position.</summary>
    public int RowOrdinal { get; }

    /// <summary>Gets the zero-based result-column position.</summary>
    public int ColumnOrdinal { get; }
}
