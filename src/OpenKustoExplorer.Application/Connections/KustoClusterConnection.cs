namespace OpenKustoExplorer.Application.Connections;

/// <summary>
/// Describes one persisted Azure Data Explorer cluster and its accessible databases.
/// </summary>
public sealed class KustoClusterConnection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoClusterConnection"/> class.
    /// </summary>
    /// <param name="clusterUri">The absolute HTTPS cluster URI.</param>
    /// <param name="displayName">The cluster display name.</param>
    /// <param name="databases">The accessible databases.</param>
    /// <param name="folderName">The optional user-defined Explorer folder name.</param>
    public KustoClusterConnection(
        Uri clusterUri,
        string displayName,
        IEnumerable<KustoDatabaseConnection> databases,
        string? folderName = null)
    {
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(databases);

        if (!clusterUri.IsAbsoluteUri || clusterUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The cluster URI must be an absolute HTTPS URI.", nameof(clusterUri));
        }

        ClusterUri = clusterUri;
        DisplayName = displayName;
        Databases = Array.AsReadOnly(databases.ToArray());
        FolderName = string.IsNullOrWhiteSpace(folderName) ? null : folderName.Trim();
    }

    /// <summary>
    /// Gets the absolute HTTPS cluster URI.
    /// </summary>
    public Uri ClusterUri { get; }

    /// <summary>
    /// Gets the cluster display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the accessible databases.
    /// </summary>
    public IReadOnlyList<KustoDatabaseConnection> Databases { get; }

    /// <summary>
    /// Gets the optional user-defined Explorer folder name.
    /// </summary>
    public string? FolderName { get; }
}
