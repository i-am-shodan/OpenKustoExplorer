namespace OpenKustoExplorer.Graph;

/// <summary>
/// Reports the durable generation and merge counts produced by one graph import.
/// </summary>
public sealed class GraphImportResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphImportResult"/> class.
    /// </summary>
    /// <param name="generationId">The active graph generation after the import.</param>
    /// <param name="ingestionId">The imported query provenance identifier.</param>
    /// <param name="evidenceCount">The number of evidence occurrences persisted.</param>
    /// <param name="entitiesAdded">The number of previously unseen entity identities.</param>
    /// <param name="entitiesObserved">The number of entity observations persisted.</param>
    /// <param name="relationshipsAdded">The number of previously unseen relationship identities.</param>
    /// <param name="relationshipsObserved">The number of relationship observations persisted.</param>
    public GraphImportResult(
        Guid generationId,
        Guid ingestionId,
        int evidenceCount,
        int entitiesAdded,
        int entitiesObserved,
        int relationshipsAdded,
        int relationshipsObserved)
        : this(
            generationId,
            generationId,
            ingestionId,
            evidenceCount,
            entitiesAdded,
            entitiesObserved,
            relationshipsAdded,
            relationshipsObserved)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphImportResult"/> class.
    /// </summary>
    /// <param name="graphId">The target named graph.</param>
    /// <param name="generationId">The current graph generation after the import.</param>
    /// <param name="ingestionId">The imported query provenance identifier.</param>
    /// <param name="evidenceCount">The number of evidence occurrences persisted.</param>
    /// <param name="entitiesAdded">The number of previously unseen entity identities.</param>
    /// <param name="entitiesObserved">The number of entity observations persisted.</param>
    /// <param name="relationshipsAdded">The number of previously unseen relationship identities.</param>
    /// <param name="relationshipsObserved">The number of relationship observations persisted.</param>
    public GraphImportResult(
        Guid graphId,
        Guid generationId,
        Guid ingestionId,
        int evidenceCount,
        int entitiesAdded,
        int entitiesObserved,
        int relationshipsAdded,
        int relationshipsObserved)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(generationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(ingestionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(evidenceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(entitiesAdded);
        ArgumentOutOfRangeException.ThrowIfNegative(entitiesObserved);
        ArgumentOutOfRangeException.ThrowIfNegative(relationshipsAdded);
        ArgumentOutOfRangeException.ThrowIfNegative(relationshipsObserved);

        GraphId = graphId;
        GenerationId = generationId;
        IngestionId = ingestionId;
        EvidenceCount = evidenceCount;
        EntitiesAdded = entitiesAdded;
        EntitiesObserved = entitiesObserved;
        RelationshipsAdded = relationshipsAdded;
        RelationshipsObserved = relationshipsObserved;
    }

    /// <summary>
    /// Gets the target named graph identifier.
    /// </summary>
    public Guid GraphId { get; }

    /// <summary>
    /// Gets the active graph generation after the import.
    /// </summary>
    public Guid GenerationId { get; }

    /// <summary>
    /// Gets the imported query provenance identifier.
    /// </summary>
    public Guid IngestionId { get; }

    /// <summary>
    /// Gets the number of evidence occurrences persisted.
    /// </summary>
    public int EvidenceCount { get; }

    /// <summary>
    /// Gets the number of previously unseen entity identities.
    /// </summary>
    public int EntitiesAdded { get; }

    /// <summary>
    /// Gets the number of entity observations persisted.
    /// </summary>
    public int EntitiesObserved { get; }

    /// <summary>
    /// Gets the number of previously unseen relationship identities.
    /// </summary>
    public int RelationshipsAdded { get; }

    /// <summary>
    /// Gets the number of relationship observations persisted.
    /// </summary>
    public int RelationshipsObserved { get; }
}
