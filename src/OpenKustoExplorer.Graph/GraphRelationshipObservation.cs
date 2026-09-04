namespace OpenKustoExplorer.Graph;

/// <summary>
/// Captures one immutable source observation of a directed relationship and its supporting evidence.
/// </summary>
public sealed class GraphRelationshipObservation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphRelationshipObservation"/> class.
    /// </summary>
    /// <param name="id">The stable observation identifier.</param>
    /// <param name="relationship">The observed directed relationship.</param>
    /// <param name="sourceLabels">The original source relationship labels.</param>
    /// <param name="properties">The source properties retained for explanation.</param>
    /// <param name="temporalInterval">The source-valid and discovery-time interval.</param>
    /// <param name="evidenceIds">Identifiers of evidence rows supporting this observation.</param>
    public GraphRelationshipObservation(
        Guid id,
        GraphRelationshipKey relationship,
        IEnumerable<string> sourceLabels,
        IReadOnlyDictionary<string, string> properties,
        GraphTemporalInterval temporalInterval,
        IEnumerable<string> evidenceIds)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentNullException.ThrowIfNull(temporalInterval);

        Id = id;
        Relationship = relationship;
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
    /// Gets the observed directed relationship.
    /// </summary>
    public GraphRelationshipKey Relationship { get; }

    /// <summary>
    /// Gets the original source relationship labels.
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
