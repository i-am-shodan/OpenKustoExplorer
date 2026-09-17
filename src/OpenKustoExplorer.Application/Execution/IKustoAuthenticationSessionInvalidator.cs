namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Invalidates process-local authentication state for one Kusto cluster authority.
/// </summary>
public interface IKustoAuthenticationSessionInvalidator
{
    /// <summary>
    /// Removes and disposes the cached authentication session for a cluster.
    /// </summary>
    /// <param name="clusterUri">The cluster whose process-local session is invalidated.</param>
    public void InvalidateClusterSession(Uri clusterUri);
}
