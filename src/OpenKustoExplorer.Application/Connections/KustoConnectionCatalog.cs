namespace OpenKustoExplorer.Application.Connections;

/// <summary>
/// Contains the persisted list of Azure Data Explorer clusters.
/// </summary>
public sealed class KustoConnectionCatalog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoConnectionCatalog"/> class.
    /// </summary>
    /// <param name="clusters">The persisted clusters.</param>
    public KustoConnectionCatalog(IEnumerable<KustoClusterConnection> clusters)
    {
        ArgumentNullException.ThrowIfNull(clusters);
        Clusters = Array.AsReadOnly(clusters.ToArray());
    }

    /// <summary>
    /// Gets the persisted clusters.
    /// </summary>
    public IReadOnlyList<KustoClusterConnection> Clusters { get; }
}
