namespace OpenKustoExplorer.Application.Updates;

/// <summary>
/// Checks the official distribution channel for a newer application release.
/// </summary>
public interface IKustoApplicationUpdateService
{
    /// <summary>
    /// Checks whether a newer stable release is available.
    /// </summary>
    /// <param name="currentVersion">The running application version.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The newer release when one exists; otherwise, <see langword="null"/>.</returns>
    public Task<KustoApplicationUpdate?> CheckForUpdateAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default);
}
