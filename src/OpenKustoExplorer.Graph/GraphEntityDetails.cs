namespace OpenKustoExplorer.Graph;

/// <summary>
/// Describes one active graph entity and its latest retained source properties.
/// </summary>
public sealed class GraphEntityDetails
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphEntityDetails"/> class.
    /// </summary>
    /// <param name="summary">The current entity summary.</param>
    /// <param name="sourceLabels">The source labels from the latest observation.</param>
    /// <param name="properties">The source properties from the latest observation.</param>
    /// <param name="observationCount">The number of retained entity observations.</param>
    /// <param name="evidenceCount">The number of distinct supporting evidence rows.</param>
    /// <param name="evidence">A bounded newest-first evidence sample.</param>
    public GraphEntityDetails(
        GraphEntitySummary summary,
        IEnumerable<string> sourceLabels,
        IEnumerable<GraphEntityProperty> properties,
        long observationCount,
        long evidenceCount,
        IEnumerable<GraphEvidenceRecord>? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(sourceLabels);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentOutOfRangeException.ThrowIfNegative(observationCount);
        ArgumentOutOfRangeException.ThrowIfNegative(evidenceCount);
        GraphEvidenceRecord[] evidenceSnapshot = evidence?.ToArray() ?? [];

        if (evidenceSnapshot.LongLength > evidenceCount)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence), "Bounded evidence cannot exceed its total count.");
        }

        Summary = summary;
        SourceLabels = Array.AsReadOnly(sourceLabels.ToArray());
        Properties = Array.AsReadOnly(properties.ToArray());
        ObservationCount = observationCount;
        EvidenceCount = evidenceCount;
        Evidence = Array.AsReadOnly(evidenceSnapshot);
    }

    /// <summary>
    /// Gets the bounded newest-first supporting evidence sample.
    /// </summary>
    public IReadOnlyList<GraphEvidenceRecord> Evidence { get; }

    /// <summary>
    /// Gets the current entity summary.
    /// </summary>
    public GraphEntitySummary Summary { get; }

    /// <summary>
    /// Gets the source labels from the latest observation.
    /// </summary>
    public IReadOnlyList<string> SourceLabels { get; }

    /// <summary>
    /// Gets the source properties from the latest observation.
    /// </summary>
    public IReadOnlyList<GraphEntityProperty> Properties { get; }

    /// <summary>
    /// Gets the number of retained entity observations.
    /// </summary>
    public long ObservationCount { get; }

    /// <summary>
    /// Gets the number of distinct supporting evidence rows.
    /// </summary>
    public long EvidenceCount { get; }

    /// <summary>
    /// Gets a value indicating whether additional evidence rows were omitted.
    /// </summary>
    public bool IsEvidenceTruncated => Evidence.Count < EvidenceCount;
}
