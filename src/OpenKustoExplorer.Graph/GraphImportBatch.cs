namespace OpenKustoExplorer.Graph;

/// <summary>
/// Contains one validated, all-or-nothing unit of graph provenance, evidence, and observations.
/// </summary>
public sealed class GraphImportBatch : IGraphImportSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphImportBatch"/> class.
    /// </summary>
    /// <param name="ingestion">The query provenance for this import.</param>
    /// <param name="evidence">The raw result-row evidence occurrences.</param>
    /// <param name="entityObservations">The entity observations produced by the evidence.</param>
    /// <param name="relationshipObservations">The relationship observations produced by the evidence.</param>
    public GraphImportBatch(
        GraphIngestion ingestion,
        IEnumerable<GraphEvidence> evidence,
        IEnumerable<GraphEntityObservation> entityObservations,
        IEnumerable<GraphRelationshipObservation> relationshipObservations)
    {
        ArgumentNullException.ThrowIfNull(ingestion);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(entityObservations);
        ArgumentNullException.ThrowIfNull(relationshipObservations);

        GraphEvidence[] evidenceSnapshot = evidence.ToArray();
        GraphEntityObservation[] entitySnapshot = entityObservations.ToArray();
        GraphRelationshipObservation[] relationshipSnapshot = relationshipObservations.ToArray();
        ValidateUniqueEvidence(evidenceSnapshot);
        ValidateObservationIds(entitySnapshot, relationshipSnapshot);
        ValidateEvidenceReferences(evidenceSnapshot, entitySnapshot, relationshipSnapshot);

        Ingestion = ingestion;
        Evidence = Array.AsReadOnly(evidenceSnapshot);
        EntityObservations = Array.AsReadOnly(entitySnapshot);
        RelationshipObservations = Array.AsReadOnly(relationshipSnapshot);
    }

    /// <summary>
    /// Gets the query provenance for this import.
    /// </summary>
    public GraphIngestion Ingestion { get; }

    /// <summary>
    /// Gets the raw result-row evidence occurrences.
    /// </summary>
    public IReadOnlyList<GraphEvidence> Evidence { get; }

    /// <summary>
    /// Gets the entity observations produced by the evidence.
    /// </summary>
    public IReadOnlyList<GraphEntityObservation> EntityObservations { get; }

    /// <summary>
    /// Gets the relationship observations produced by the evidence.
    /// </summary>
    public IReadOnlyList<GraphRelationshipObservation> RelationshipObservations { get; }

    /// <inheritdoc />
    public IEnumerable<GraphEvidence> GetEvidence() => Evidence;

    /// <inheritdoc />
    public IEnumerable<GraphEntityObservation> GetEntityObservations() => EntityObservations;

    /// <inheritdoc />
    public IEnumerable<GraphRelationshipObservation> GetRelationshipObservations() => RelationshipObservations;

    private static void ValidateEvidenceReferences(
        IReadOnlyList<GraphEvidence> evidence,
        IReadOnlyList<GraphEntityObservation> entityObservations,
        IReadOnlyList<GraphRelationshipObservation> relationshipObservations)
    {
        HashSet<string> evidenceIds = evidence
            .Select(item => item.OccurrenceId)
            .ToHashSet(StringComparer.Ordinal);
        string? missingEvidenceId = entityObservations
            .SelectMany(observation => observation.EvidenceIds)
            .Concat(relationshipObservations.SelectMany(observation => observation.EvidenceIds))
            .FirstOrDefault(evidenceId => !evidenceIds.Contains(evidenceId));

        if (missingEvidenceId is not null)
        {
            throw new ArgumentException(
                $"Observation evidence '{missingEvidenceId}' is not included in the import batch.",
                nameof(evidence));
        }
    }

    private static void ValidateObservationIds(
        IReadOnlyList<GraphEntityObservation> entityObservations,
        IReadOnlyList<GraphRelationshipObservation> relationshipObservations)
    {
        Guid? duplicateId = entityObservations
            .Select(observation => observation.Id)
            .Concat(relationshipObservations.Select(observation => observation.Id))
            .GroupBy(id => id)
            .Where(group => group.Count() > 1)
            .Select(group => (Guid?)group.Key)
            .FirstOrDefault();

        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Observation identifier '{duplicateId}' appears more than once in the import batch.",
                nameof(entityObservations));
        }
    }

    private static void ValidateUniqueEvidence(IReadOnlyList<GraphEvidence> evidence)
    {
        string? duplicateId = evidence
            .GroupBy(item => item.OccurrenceId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();

        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Evidence occurrence '{duplicateId}' appears more than once in the import batch.",
                nameof(evidence));
        }
    }
}
