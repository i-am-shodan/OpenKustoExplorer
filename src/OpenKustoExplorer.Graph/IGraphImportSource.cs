namespace OpenKustoExplorer.Graph;

/// <summary>
/// Supplies one validated graph ingestion as repeatable, bounded-memory sequences.
/// </summary>
/// <remarks>
/// The source must remain valid until
/// <see cref="IGraphStore.ImportAsync(IGraphImportSource, GraphImportMode, CancellationToken)"/> completes. Implementations may
/// enumerate staged local storage rather than retaining the complete ingestion in memory.
/// </remarks>
public interface IGraphImportSource
{
    /// <summary>
    /// Gets the query or automation provenance for this ingestion.
    /// </summary>
    public GraphIngestion Ingestion { get; }

    /// <summary>
    /// Enumerates every raw evidence occurrence exactly once.
    /// </summary>
    /// <returns>The evidence occurrences.</returns>
    public IEnumerable<GraphEvidence> GetEvidence();

    /// <summary>
    /// Enumerates every entity observation exactly once.
    /// </summary>
    /// <returns>The entity observations.</returns>
    public IEnumerable<GraphEntityObservation> GetEntityObservations();

    /// <summary>
    /// Enumerates every relationship observation exactly once.
    /// </summary>
    /// <returns>The relationship observations.</returns>
    public IEnumerable<GraphRelationshipObservation> GetRelationshipObservations();
}
