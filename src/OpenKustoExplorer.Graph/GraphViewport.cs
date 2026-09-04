namespace OpenKustoExplorer.Graph;

/// <summary>
/// Contains a bounded set of active-generation entities and relationships suitable for layout and rendering.
/// </summary>
public sealed class GraphViewport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphViewport"/> class.
    /// </summary>
    /// <param name="center">The requested or automatically selected center entity.</param>
    /// <param name="entities">The bounded entity summaries.</param>
    /// <param name="relationships">Relationships whose endpoints are present in <paramref name="entities"/>.</param>
    /// <param name="isTruncated">Whether additional neighboring data was omitted by viewport limits.</param>
    public GraphViewport(
        GraphEntityKey? center,
        IEnumerable<GraphEntitySummary> entities,
        IEnumerable<GraphRelationshipKey> relationships,
        bool isTruncated)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(relationships);
        GraphEntitySummary[] entitySnapshot = entities.ToArray();
        GraphRelationshipKey[] relationshipSnapshot = relationships.ToArray();
        HashSet<GraphEntityKey> entityKeys = entitySnapshot
            .Select(entity => entity.Entity)
            .ToHashSet();

        if (entityKeys.Count != entitySnapshot.Length)
        {
            throw new ArgumentException("A graph viewport cannot contain duplicate entities.", nameof(entities));
        }

        if (center is GraphEntityKey centerEntity && !entityKeys.Contains(centerEntity))
        {
            throw new ArgumentException("The graph viewport center must be included in its entities.", nameof(center));
        }

        bool hasInvalidRelationship = relationshipSnapshot.Any(
            relationship => !entityKeys.Contains(relationship.Source)
                || !entityKeys.Contains(relationship.Target));
        if (hasInvalidRelationship)
        {
            throw new ArgumentException(
                "Every graph viewport relationship endpoint must be included in its entities.",
                nameof(relationships));
        }

        if (relationshipSnapshot.Distinct().Count() != relationshipSnapshot.Length)
        {
            throw new ArgumentException(
                "A graph viewport cannot contain duplicate relationships.",
                nameof(relationships));
        }

        Center = center;
        Entities = Array.AsReadOnly(entitySnapshot);
        Relationships = Array.AsReadOnly(relationshipSnapshot);
        IsTruncated = isTruncated;
    }

    /// <summary>
    /// Gets the requested or automatically selected center entity.
    /// </summary>
    public GraphEntityKey? Center { get; }

    /// <summary>
    /// Gets the bounded entity summaries.
    /// </summary>
    public IReadOnlyList<GraphEntitySummary> Entities { get; }

    /// <summary>
    /// Gets a value indicating whether no entities are available to render.
    /// </summary>
    public bool IsEmpty => Entities.Count == 0;

    /// <summary>
    /// Gets a value indicating whether additional neighboring data was omitted by viewport limits.
    /// </summary>
    public bool IsTruncated { get; }

    /// <summary>
    /// Gets relationships whose endpoints are present in <see cref="Entities"/>.
    /// </summary>
    public IReadOnlyList<GraphRelationshipKey> Relationships { get; }
}
