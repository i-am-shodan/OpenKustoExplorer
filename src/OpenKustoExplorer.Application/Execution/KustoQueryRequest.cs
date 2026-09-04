namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Identifies one KQL request and its target cluster and database.
/// </summary>
public sealed class KustoQueryRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoQueryRequest"/> class.
    /// </summary>
    /// <param name="clusterUri">The absolute HTTPS cluster URI.</param>
    /// <param name="databaseName">The target database name.</param>
    /// <param name="queryText">The KQL text to execute.</param>
    public KustoQueryRequest(Uri clusterUri, string databaseName, string queryText)
    {
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);

        if (!clusterUri.IsAbsoluteUri || clusterUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The cluster URI must be an absolute HTTPS URI.", nameof(clusterUri));
        }

        ClusterUri = clusterUri;
        DatabaseName = databaseName;
        QueryText = queryText;
    }

    /// <summary>
    /// Gets the absolute HTTPS cluster URI.
    /// </summary>
    public Uri ClusterUri { get; }

    /// <summary>
    /// Gets the target database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the KQL text to execute.
    /// </summary>
    public string QueryText { get; }
}
