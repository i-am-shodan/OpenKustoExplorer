namespace OpenKustoExplorer.Graph;

/// <summary>
/// Describes the complete query provenance for one atomic graph ingestion.
/// </summary>
public sealed class GraphIngestion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphIngestion"/> class.
    /// </summary>
    /// <param name="id">The stable ingestion identifier.</param>
    /// <param name="sourceKind">The workflow that initiated the query.</param>
    /// <param name="sourceId">The optional document or automation identifier.</param>
    /// <param name="sourceName">The analyst-facing document or automation name.</param>
    /// <param name="clusterUri">The source Azure Data Explorer cluster.</param>
    /// <param name="databaseName">The source database.</param>
    /// <param name="queryText">The complete selected KQL block that created the graph data.</param>
    /// <param name="startedAtUtc">When query execution started.</param>
    /// <param name="completedAtUtc">When query execution completed.</param>
    public GraphIngestion(
        Guid id,
        GraphIngestionSourceKind sourceKind,
        Guid? sourceId,
        string sourceName,
        Uri clusterUri,
        string databaseName,
        string queryText,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);

        if (!clusterUri.IsAbsoluteUri || clusterUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The ingestion cluster URI must be absolute HTTPS.", nameof(clusterUri));
        }

        DateTimeOffset normalizedStart = startedAtUtc.ToUniversalTime();
        DateTimeOffset normalizedCompletion = completedAtUtc.ToUniversalTime();
        if (normalizedCompletion < normalizedStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAtUtc),
                "Ingestion completion cannot precede its start.");
        }

        Id = id;
        SourceKind = sourceKind;
        SourceId = sourceId;
        SourceName = sourceName.Trim();
        ClusterUri = clusterUri;
        DatabaseName = databaseName.Trim();
        QueryText = queryText.Trim();
        StartedAtUtc = normalizedStart;
        CompletedAtUtc = normalizedCompletion;
    }

    /// <summary>
    /// Gets the stable ingestion identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the workflow that initiated the query.
    /// </summary>
    public GraphIngestionSourceKind SourceKind { get; }

    /// <summary>
    /// Gets the optional document or automation identifier.
    /// </summary>
    public Guid? SourceId { get; }

    /// <summary>
    /// Gets the analyst-facing document or automation name.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Gets the source Azure Data Explorer cluster.
    /// </summary>
    public Uri ClusterUri { get; }

    /// <summary>
    /// Gets the source database.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the complete selected KQL block that created the graph data.
    /// </summary>
    public string QueryText { get; }

    /// <summary>
    /// Gets when query execution started.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; }

    /// <summary>
    /// Gets when query execution completed.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }
}
