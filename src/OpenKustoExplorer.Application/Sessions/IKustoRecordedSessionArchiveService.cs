namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Exports and imports portable recorded-session archives.
/// </summary>
public interface IKustoRecordedSessionArchiveService
{
    /// <summary>Exports one finalized recorded session.</summary>
    /// <param name="sessionId">The session to export.</param>
    /// <param name="destination">The writable archive destination.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes after the archive is written.</returns>
    public Task ExportAsync(
        Guid sessionId,
        Stream destination,
        CancellationToken cancellationToken = default);

    /// <summary>Imports an archive as an independent local session.</summary>
    /// <param name="source">The readable archive source.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The imported session summary.</returns>
    public Task<KustoRecordedSessionSummary> ImportCopyAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
