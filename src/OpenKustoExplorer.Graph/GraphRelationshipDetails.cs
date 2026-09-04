namespace OpenKustoExplorer.Graph;

/// <summary>
/// Describes one retained relationship, its latest properties, and bounded supporting evidence.
/// </summary>
public sealed class GraphRelationshipDetails
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphRelationshipDetails"/> class.
    /// </summary>
    /// <param name="relationship">The retained relationship identity.</param>
    /// <param name="sourceLabels">The source labels from the latest observation.</param>
    /// <param name="properties">The source properties from the latest observation.</param>
    /// <param name="firstDiscoveredAtUtc">When the relationship was first discovered.</param>
    /// <param name="lastUpdatedAtUtc">When the relationship was most recently updated.</param>
    /// <param name="observationCount">The number of retained observations.</param>
    /// <param name="evidenceCount">The total number of distinct supporting evidence rows.</param>
    /// <param name="evidence">A bounded newest-first evidence sample.</param>
    public GraphRelationshipDetails(
        GraphRelationshipKey relationship,
        IEnumerable<string> sourceLabels,
        IEnumerable<GraphEntityProperty> properties,
        DateTimeOffset firstDiscoveredAtUtc,
        DateTimeOffset lastUpdatedAtUtc,
        long observationCount,
        long evidenceCount,
        IEnumerable<GraphEvidenceRecord> evidence)
    {
        ArgumentNullException.ThrowIfNull(sourceLabels);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentOutOfRangeException.ThrowIfNegative(observationCount);
        ArgumentOutOfRangeException.ThrowIfNegative(evidenceCount);
        DateTimeOffset normalizedFirst = firstDiscoveredAtUtc.ToUniversalTime();
        DateTimeOffset normalizedLast = lastUpdatedAtUtc.ToUniversalTime();

        if (normalizedLast < normalizedFirst)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastUpdatedAtUtc),
                "Relationship update time cannot precede discovery.");
        }

        GraphEvidenceRecord[] evidenceSnapshot = evidence.ToArray();
        if (evidenceSnapshot.LongLength > evidenceCount)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence), "Bounded evidence cannot exceed its total count.");
        }

        Relationship = relationship;
        SourceLabels = Array.AsReadOnly(sourceLabels.ToArray());
        Properties = Array.AsReadOnly(properties.ToArray());
        FirstDiscoveredAtUtc = normalizedFirst;
        LastUpdatedAtUtc = normalizedLast;
        ObservationCount = observationCount;
        EvidenceCount = evidenceCount;
        Evidence = Array.AsReadOnly(evidenceSnapshot);
    }

    /// <summary>
    /// Gets the bounded newest-first supporting evidence sample.
    /// </summary>
    public IReadOnlyList<GraphEvidenceRecord> Evidence { get; }

    /// <summary>
    /// Gets the total number of distinct supporting evidence rows.
    /// </summary>
    public long EvidenceCount { get; }

    /// <summary>
    /// Gets when the relationship was first discovered.
    /// </summary>
    public DateTimeOffset FirstDiscoveredAtUtc { get; }

    /// <summary>
    /// Gets a value indicating whether additional evidence rows were omitted.
    /// </summary>
    public bool IsEvidenceTruncated => Evidence.Count < EvidenceCount;

    /// <summary>
    /// Gets when the relationship was most recently updated.
    /// </summary>
    public DateTimeOffset LastUpdatedAtUtc { get; }

    /// <summary>
    /// Gets the number of retained relationship observations.
    /// </summary>
    public long ObservationCount { get; }

    /// <summary>
    /// Gets the source properties from the latest observation.
    /// </summary>
    public IReadOnlyList<GraphEntityProperty> Properties { get; }

    /// <summary>
    /// Gets the retained relationship identity.
    /// </summary>
    public GraphRelationshipKey Relationship { get; }

    /// <summary>
    /// Gets the source labels from the latest observation.
    /// </summary>
    public IReadOnlyList<string> SourceLabels { get; }
}
