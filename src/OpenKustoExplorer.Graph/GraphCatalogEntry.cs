namespace OpenKustoExplorer.Graph;

/// <summary>
/// Summarizes one durable named graph and its current generation.
/// </summary>
public sealed class GraphCatalogEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphCatalogEntry"/> class.
    /// </summary>
    /// <param name="graphId">The durable graph identifier.</param>
    /// <param name="name">The analyst-facing graph name.</param>
    /// <param name="description">The optional graph description.</param>
    /// <param name="generationId">The graph's current generation identifier.</param>
    /// <param name="createdAtUtc">When the graph was created.</param>
    /// <param name="lastUpdatedAtUtc">When the graph or its current generation last changed.</param>
    /// <param name="lastActivatedAtUtc">When the graph was most recently activated.</param>
    /// <param name="entityCount">The current generation entity count.</param>
    /// <param name="relationshipCount">The current generation relationship count.</param>
    public GraphCatalogEntry(
        Guid graphId,
        string name,
        string? description,
        Guid generationId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastUpdatedAtUtc,
        DateTimeOffset lastActivatedAtUtc,
        long entityCount,
        long relationshipCount)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(generationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(relationshipCount);
        string normalizedName = GraphCatalogMetadata.NormalizeName(name);
        string normalizedDescription = GraphCatalogMetadata.NormalizeDescription(description);
        DateTimeOffset normalizedCreation = createdAtUtc.ToUniversalTime();
        DateTimeOffset normalizedUpdate = lastUpdatedAtUtc.ToUniversalTime();
        DateTimeOffset normalizedActivation = lastActivatedAtUtc.ToUniversalTime();

        if (normalizedUpdate < normalizedCreation)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastUpdatedAtUtc),
                "Graph update time cannot precede graph creation.");
        }

        if (normalizedActivation < normalizedCreation)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastActivatedAtUtc),
                "Graph activation time cannot precede graph creation.");
        }

        GraphId = graphId;
        Name = normalizedName;
        Description = normalizedDescription;
        GenerationId = generationId;
        CreatedAtUtc = normalizedCreation;
        LastUpdatedAtUtc = normalizedUpdate;
        LastActivatedAtUtc = normalizedActivation;
        EntityCount = entityCount;
        RelationshipCount = relationshipCount;
    }

    /// <summary>
    /// Gets the durable graph identifier.
    /// </summary>
    public Guid GraphId { get; }

    /// <summary>
    /// Gets the analyst-facing graph name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the optional graph description, or an empty string.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the graph's current generation identifier.
    /// </summary>
    public Guid GenerationId { get; }

    /// <summary>
    /// Gets the immutable current graph snapshot.
    /// </summary>
    public GraphSnapshot Snapshot => new(GraphId, GenerationId);

    /// <summary>
    /// Gets when the graph was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets when the graph or its current generation last changed.
    /// </summary>
    public DateTimeOffset LastUpdatedAtUtc { get; }

    /// <summary>
    /// Gets when the graph was most recently activated.
    /// </summary>
    public DateTimeOffset LastActivatedAtUtc { get; }

    /// <summary>
    /// Gets the current generation entity count.
    /// </summary>
    public long EntityCount { get; }

    /// <summary>
    /// Gets the current generation relationship count.
    /// </summary>
    public long RelationshipCount { get; }

    /// <summary>
    /// Gets a value indicating whether the current generation is empty.
    /// </summary>
    public bool IsEmpty => EntityCount == 0 && RelationshipCount == 0;
}
