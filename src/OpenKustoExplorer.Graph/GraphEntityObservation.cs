namespace OpenKustoExplorer.Graph;

/// <summary>
/// Captures one immutable source observation of a graph entity and its supporting evidence.
/// </summary>
public sealed class GraphEntityObservation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphEntityObservation"/> class.
    /// </summary>
    /// <param name="id">The stable observation identifier.</param>
    /// <param name="entity">The observed entity.</param>
    /// <param name="displayLabel">The preferred display label at this observation.</param>
    /// <param name="sourceLabels">The original source type labels.</param>
    /// <param name="properties">The source properties retained for explanation.</param>
    /// <param name="temporalInterval">The source-valid and discovery-time interval.</param>
    /// <param name="evidenceIds">Identifiers of evidence rows supporting this observation.</param>
    public GraphEntityObservation(
        Guid id,
        GraphEntityKey entity,
        string displayLabel,
        IEnumerable<string> sourceLabels,
        IReadOnlyDictionary<string, string> properties,
        GraphTemporalInterval temporalInterval,
        IEnumerable<string> evidenceIds)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLabel);
        ArgumentNullException.ThrowIfNull(temporalInterval);

        Id = id;
        Entity = entity;
        DisplayLabel = displayLabel.Trim();
        SourceLabels = GraphCollections.CopyStrings(sourceLabels);
        Properties = GraphCollections.CopyProperties(properties);
        TemporalInterval = temporalInterval;
        EvidenceIds = GraphCollections.CopyStrings(evidenceIds);
    }

    /// <summary>
    /// Gets the stable observation identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the observed entity.
    /// </summary>
    public GraphEntityKey Entity { get; }

    /// <summary>
    /// Gets the preferred display label at this observation.
    /// </summary>
    public string DisplayLabel { get; }

    /// <summary>
    /// Gets the original source type labels.
    /// </summary>
    public IReadOnlyList<string> SourceLabels { get; }

    /// <summary>
    /// Gets the source properties retained for explanation.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; }

    /// <summary>
    /// Gets the source-valid and discovery-time interval.
    /// </summary>
    public GraphTemporalInterval TemporalInterval { get; }

    /// <summary>
    /// Gets identifiers of evidence rows supporting this observation.
    /// </summary>
    public IReadOnlyList<string> EvidenceIds { get; }
}
