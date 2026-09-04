namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Receives one graph export table at a time without retaining the complete query response in memory.
/// </summary>
public interface IKustoGraphExportSink
{
    /// <summary>
    /// Begins an exported graph table after its schema is known.
    /// </summary>
    /// <param name="kind">Whether the table contains nodes or edges.</param>
    /// <param name="tableName">The generated result-table name.</param>
    /// <param name="columns">The ordered result columns.</param>
    public void BeginTable(
        KustoGraphExportTableKind kind,
        string tableName,
        IReadOnlyList<KustoResultColumn> columns);

    /// <summary>
    /// Completes the current exported graph table.
    /// </summary>
    /// <param name="kind">Whether the table contains nodes or edges.</param>
    public void EndTable(KustoGraphExportTableKind kind);

    /// <summary>
    /// Writes one ordered row to the current table.
    /// </summary>
    /// <param name="kind">Whether the row contains a node or edge.</param>
    /// <param name="values">The ordered invariant string cell values.</param>
    public void WriteRow(KustoGraphExportTableKind kind, IReadOnlyList<string> values);
}
