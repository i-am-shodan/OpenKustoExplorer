namespace OpenKustoExplorer.Graph;

/// <summary>
/// Preserves one query-result row occurrence used to explain graph entities or relationships.
/// </summary>
public sealed class GraphEvidence
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphEvidence"/> class.
    /// </summary>
    /// <param name="occurrenceId">The stable identifier for this discovery occurrence.</param>
    /// <param name="contentHash">The deterministic hash used to deduplicate identical payloads.</param>
    /// <param name="tableName">The source result-table name.</param>
    /// <param name="rowOrdinal">The zero-based row position within the source result table.</param>
    /// <param name="schemaJson">The canonical source table schema JSON.</param>
    /// <param name="rowJson">The canonical raw row JSON.</param>
    public GraphEvidence(
        string occurrenceId,
        string contentHash,
        string tableName,
        int rowOrdinal,
        string schemaJson,
        string rowJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrenceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentOutOfRangeException.ThrowIfNegative(rowOrdinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(rowJson);

        OccurrenceId = occurrenceId.Trim();
        ContentHash = contentHash.Trim();
        TableName = tableName.Trim();
        RowOrdinal = rowOrdinal;
        SchemaJson = schemaJson;
        RowJson = rowJson;
    }

    /// <summary>
    /// Gets the stable identifier for this discovery occurrence.
    /// </summary>
    public string OccurrenceId { get; }

    /// <summary>
    /// Gets the deterministic hash used to deduplicate identical payloads.
    /// </summary>
    public string ContentHash { get; }

    /// <summary>
    /// Gets the source result-table name.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Gets the zero-based row position within the source result table.
    /// </summary>
    public int RowOrdinal { get; }

    /// <summary>
    /// Gets the canonical source table schema JSON.
    /// </summary>
    public string SchemaJson { get; }

    /// <summary>
    /// Gets the canonical raw row JSON.
    /// </summary>
    public string RowJson { get; }
}
