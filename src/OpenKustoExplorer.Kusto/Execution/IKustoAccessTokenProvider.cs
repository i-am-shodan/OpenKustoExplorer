namespace OpenKustoExplorer.Kusto.Execution;

/// <summary>
/// Supplies an access token for an already validated Kusto cluster authority.
/// </summary>
public interface IKustoAccessTokenProvider
{
    /// <summary>
    /// Gets an access token whose audience permits access to the supplied cluster.
    /// </summary>
    /// <param name="clusterUri">The validated cluster authority.</param>
    /// <param name="cancellationToken">A token that cancels token acquisition.</param>
    /// <returns>The bearer access token.</returns>
    public Task<string> GetAccessTokenAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default);
}
