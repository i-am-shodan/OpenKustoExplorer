using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Finds a bounded, weighted inferred pivot chain through a recorded session.
/// </summary>
public interface IKustoRecordedChainSearcher
{
    /// <summary>
    /// Finds the strongest bounded path between two exact recorded result coordinates.
    /// </summary>
    /// <param name="sessionId">The recorded session identifier.</param>
    /// <param name="start">The start result coordinate.</param>
    /// <param name="destination">The destination result coordinate.</param>
    /// <param name="cancellationToken">Cancels traversal.</param>
    /// <returns>The inferred chain, or <see langword="null"/> when no path exists.</returns>
    public Task<KustoQueryChain?> FindAsync(
        Guid sessionId,
        KustoRecordedValueCoordinate start,
        KustoRecordedValueCoordinate destination,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the strongest bounded path and re-analyzes legacy queries missing relation metadata.
    /// </summary>
    /// <param name="sessionId">The recorded session identifier.</param>
    /// <param name="start">The start result coordinate.</param>
    /// <param name="destination">The destination result coordinate.</param>
    /// <param name="databaseSchema">The active database schema used to analyze legacy queries.</param>
    /// <param name="cancellationToken">Cancels traversal.</param>
    /// <returns>The inferred chain, or <see langword="null"/> when no path exists.</returns>
    public Task<KustoQueryChain?> FindAsync(
        Guid sessionId,
        KustoRecordedValueCoordinate start,
        KustoRecordedValueCoordinate destination,
        KustoDatabaseSchema databaseSchema,
        CancellationToken cancellationToken = default);
}
