namespace OpenKustoExplorer.Application.Connections;

/// <summary>
/// Contains validated legacy connections, open tabs, and skipped-entry counts.
/// </summary>
public sealed class KustoExplorerImportResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoExplorerImportResult"/> class.
    /// </summary>
    /// <param name="sourceFound">Whether the legacy profile directory exists.</param>
    /// <param name="connections">The validated, deduplicated connections.</param>
    /// <param name="skippedConnectionCount">The number of invalid or duplicate source entries.</param>
    /// <param name="tabs">All decodable open query tabs in source order.</param>
    /// <param name="skippedTabCount">The number of malformed recovery tabs that could not be imported.</param>
    public KustoExplorerImportResult(
        bool sourceFound,
        IEnumerable<KustoClusterConnection> connections,
        int skippedConnectionCount,
        IEnumerable<KustoExplorerImportedTab>? tabs = null,
        int skippedTabCount = 0)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentOutOfRangeException.ThrowIfNegative(skippedConnectionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(skippedTabCount);

        SourceFound = sourceFound;
        Connections = Array.AsReadOnly(connections.ToArray());
        SkippedConnectionCount = skippedConnectionCount;
        Tabs = Array.AsReadOnly(tabs?.OrderBy(tab => tab.Order).ToArray() ?? []);
        SkippedTabCount = skippedTabCount;
    }

    /// <summary>
    /// Gets a value indicating whether the legacy Kusto Explorer profile directory exists.
    /// </summary>
    public bool SourceFound { get; }

    /// <summary>
    /// Gets validated connections in source order.
    /// </summary>
    public IReadOnlyList<KustoClusterConnection> Connections { get; }

    /// <summary>
    /// Gets the number of invalid or duplicate source entries.
    /// </summary>
    public int SkippedConnectionCount { get; }

    /// <summary>
    /// Gets all decodable open query tabs in source order.
    /// </summary>
    public IReadOnlyList<KustoExplorerImportedTab> Tabs { get; }

    /// <summary>
    /// Gets the number of malformed recovery tabs that could not be imported.
    /// </summary>
    public int SkippedTabCount { get; }
}
