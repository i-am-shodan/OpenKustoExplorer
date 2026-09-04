namespace OpenKustoExplorer.Graph;

/// <summary>
/// Describes one retained source row and the query ingestion that produced it.
/// </summary>
public sealed class GraphEvidenceRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphEvidenceRecord"/> class.
    /// </summary>
    /// <param name="ingestionId">The stable ingestion identifier.</param>
    /// <param name="sourceKind">The workflow that initiated the ingestion.</param>
    /// <param name="sourceId">The optional source document or automation identifier.</param>
    /// <param name="sourceName">The analyst-facing source name.</param>
    /// <param name="clusterUri">The source Kusto cluster.</param>
    /// <param name="databaseName">The source Kusto database.</param>
    /// <param name="queryText">The complete selected KQL block.</param>
    /// <param name="ingestedAtUtc">When the ingestion completed.</param>
    /// <param name="discoveredAtUtc">When this observation was discovered.</param>
    /// <param name="tableName">The source result-table name.</param>
    /// <param name="rowOrdinal">The zero-based source row position.</param>
    /// <param name="schemaJson">The canonical source schema JSON.</param>
    /// <param name="rowJson">The canonical raw row JSON.</param>
    public GraphEvidenceRecord(
        Guid ingestionId,
        GraphIngestionSourceKind sourceKind,
        Guid? sourceId,
        string sourceName,
        Uri clusterUri,
        string databaseName,
        string queryText,
        DateTimeOffset ingestedAtUtc,
        DateTimeOffset discoveredAtUtc,
        string tableName,
        int rowOrdinal,
        string schemaJson,
        string rowJson)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ingestionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(rowOrdinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(rowJson);

        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceId));
        }

        if (!clusterUri.IsAbsoluteUri || !string.Equals(clusterUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The evidence cluster URI must be absolute HTTPS.", nameof(clusterUri));
        }

        IngestionId = ingestionId;
        SourceKind = sourceKind;
        SourceId = sourceId;
        SourceName = sourceName.Trim();
        ClusterUri = clusterUri;
        DatabaseName = databaseName.Trim();
        QueryText = queryText.Trim();
        IngestedAtUtc = ingestedAtUtc.ToUniversalTime();
        DiscoveredAtUtc = discoveredAtUtc.ToUniversalTime();
        TableName = tableName.Trim();
        RowOrdinal = rowOrdinal;
        SchemaJson = schemaJson;
        RowJson = rowJson;
    }

    /// <summary>
    /// Gets the source Kusto cluster.
    /// </summary>
    public Uri ClusterUri { get; }

    /// <summary>
    /// Gets the source Kusto database.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets when this observation was discovered.
    /// </summary>
    public DateTimeOffset DiscoveredAtUtc { get; }

    /// <summary>
    /// Gets the stable ingestion identifier.
    /// </summary>
    public Guid IngestionId { get; }

    /// <summary>
    /// Gets when the source ingestion completed.
    /// </summary>
    public DateTimeOffset IngestedAtUtc { get; }

    /// <summary>
    /// Gets the complete selected KQL block.
    /// </summary>
    public string QueryText { get; }

    /// <summary>
    /// Gets the zero-based source row position.
    /// </summary>
    public int RowOrdinal { get; }

    /// <summary>
    /// Gets the canonical raw row JSON.
    /// </summary>
    public string RowJson { get; }

    /// <summary>
    /// Gets the canonical source schema JSON.
    /// </summary>
    public string SchemaJson { get; }

    /// <summary>
    /// Gets the optional source document or automation identifier.
    /// </summary>
    public Guid? SourceId { get; }

    /// <summary>
    /// Gets the workflow that initiated the ingestion.
    /// </summary>
    public GraphIngestionSourceKind SourceKind { get; }

    /// <summary>
    /// Gets the analyst-facing source name.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Gets the source result-table name.
    /// </summary>
    public string TableName { get; }
}
