namespace OpenKustoExplorer.Graph;

/// <summary>
/// Summarizes one durable named graph generation without materializing its contents.
/// </summary>
public sealed class GraphStateSummary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphStateSummary"/> class.
    /// </summary>
    /// <param name="generationId">The active graph generation identifier.</param>
    /// <param name="createdAtUtc">When the active generation was created.</param>
    /// <param name="lastUpdatedAtUtc">When the active generation last changed.</param>
    /// <param name="entityCount">The number of unique entity identities.</param>
    /// <param name="relationshipCount">The number of unique relationship identities.</param>
    /// <param name="ingestionCount">The number of committed query ingestions.</param>
    /// <param name="evidenceCount">The number of evidence occurrences.</param>
    public GraphStateSummary(
        Guid generationId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastUpdatedAtUtc,
        long entityCount,
        long relationshipCount,
        long ingestionCount,
        long evidenceCount)
        : this(
            generationId,
            "Default graph",
            string.Empty,
            generationId,
            createdAtUtc,
            lastUpdatedAtUtc,
            entityCount,
            relationshipCount,
            ingestionCount,
            evidenceCount)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphStateSummary"/> class.
    /// </summary>
    /// <param name="graphId">The named graph identifier.</param>
    /// <param name="graphName">The analyst-facing graph name.</param>
    /// <param name="graphDescription">The optional graph description.</param>
    /// <param name="generationId">The current graph generation identifier.</param>
    /// <param name="createdAtUtc">When the current generation was created.</param>
    /// <param name="lastUpdatedAtUtc">When the current generation last changed.</param>
    /// <param name="entityCount">The number of unique entity identities.</param>
    /// <param name="relationshipCount">The number of unique relationship identities.</param>
    /// <param name="ingestionCount">The number of committed query ingestions.</param>
    /// <param name="evidenceCount">The number of evidence occurrences.</param>
    public GraphStateSummary(
        Guid graphId,
        string graphName,
        string? graphDescription,
        Guid generationId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastUpdatedAtUtc,
        long entityCount,
        long relationshipCount,
        long ingestionCount,
        long evidenceCount)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(generationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(relationshipCount);
        ArgumentOutOfRangeException.ThrowIfNegative(ingestionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(evidenceCount);

        DateTimeOffset normalizedCreation = createdAtUtc.ToUniversalTime();
        DateTimeOffset normalizedUpdate = lastUpdatedAtUtc.ToUniversalTime();
        if (normalizedUpdate < normalizedCreation)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastUpdatedAtUtc),
                "Graph update time cannot precede generation creation.");
        }

        GraphId = graphId;
        GraphName = GraphCatalogMetadata.NormalizeName(graphName);
        GraphDescription = GraphCatalogMetadata.NormalizeDescription(graphDescription);
        GenerationId = generationId;
        CreatedAtUtc = normalizedCreation;
        LastUpdatedAtUtc = normalizedUpdate;
        EntityCount = entityCount;
        RelationshipCount = relationshipCount;
        IngestionCount = ingestionCount;
        EvidenceCount = evidenceCount;
    }

    /// <summary>
    /// Gets the durable named graph identifier.
    /// </summary>
    public Guid GraphId { get; }

    /// <summary>
    /// Gets the analyst-facing graph name.
    /// </summary>
    public string GraphName { get; }

    /// <summary>
    /// Gets the optional graph description, or an empty string.
    /// </summary>
    public string GraphDescription { get; }

    /// <summary>
    /// Gets the active graph generation identifier.
    /// </summary>
    public Guid GenerationId { get; }

    /// <summary>
    /// Gets the immutable graph snapshot represented by this state.
    /// </summary>
    public GraphSnapshot Snapshot => new(GraphId, GenerationId);

    /// <summary>
    /// Gets when the active generation was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets when the active generation last changed.
    /// </summary>
    public DateTimeOffset LastUpdatedAtUtc { get; }

    /// <summary>
    /// Gets the number of unique entity identities.
    /// </summary>
    public long EntityCount { get; }

    /// <summary>
    /// Gets the number of unique relationship identities.
    /// </summary>
    public long RelationshipCount { get; }

    /// <summary>
    /// Gets the number of committed query ingestions.
    /// </summary>
    public long IngestionCount { get; }

    /// <summary>
    /// Gets the number of evidence occurrences.
    /// </summary>
    public long EvidenceCount { get; }

    /// <summary>
    /// Gets a value indicating whether the active graph contains no entities or relationships.
    /// </summary>
    public bool IsEmpty => EntityCount == 0 && RelationshipCount == 0;
}
