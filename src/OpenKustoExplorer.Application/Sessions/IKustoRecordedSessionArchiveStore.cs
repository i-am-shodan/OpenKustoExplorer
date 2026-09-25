namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Persists complete recorded-session aggregates used by portable archives.
/// </summary>
public interface IKustoRecordedSessionArchiveStore : IKustoRecordedSessionStore
{
    /// <summary>Imports a validated session aggregate as an independent copy.</summary>
    /// <param name="source">The source session aggregate.</param>
    /// <param name="cancellationToken">Cancels and rolls back the import.</param>
    /// <returns>The imported session summary.</returns>
    public Task<KustoRecordedSessionSummary> ImportSessionCopyAsync(
        KustoRecordedSession source,
        CancellationToken cancellationToken = default);
}
