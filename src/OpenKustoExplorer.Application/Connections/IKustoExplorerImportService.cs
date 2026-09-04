namespace OpenKustoExplorer.Application.Connections;

/// <summary>
/// Imports safe connection metadata from a local Microsoft Kusto Explorer profile.
/// </summary>
public interface IKustoExplorerImportService
{
    /// <summary>
    /// Reads validated cluster connections and open tabs without importing credentials, cached results, history, or settings.
    /// </summary>
    /// <param name="cancellationToken">Cancels local profile parsing.</param>
    /// <returns>The import result.</returns>
    public Task<KustoExplorerImportResult> ImportConnectionsAsync(
        CancellationToken cancellationToken = default);
}
