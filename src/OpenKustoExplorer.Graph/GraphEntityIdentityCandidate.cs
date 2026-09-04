namespace OpenKustoExplorer.Graph;

/// <summary>
/// Describes one graph identity participating in a possible duplicate-node match.
/// </summary>
public sealed class GraphEntityIdentityCandidate
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphEntityIdentityCandidate"/> class.
    /// </summary>
    /// <param name="entity">The candidate entity identity.</param>
    /// <param name="displayLabel">The candidate display label.</param>
    /// <param name="occurrenceCount">The number of staged or retained occurrences.</param>
    /// <param name="isExisting">Whether the candidate already exists in the target graph.</param>
    public GraphEntityIdentityCandidate(
        GraphEntityKey entity,
        string displayLabel,
        int occurrenceCount,
        bool isExisting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLabel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(occurrenceCount);

        Entity = entity;
        DisplayLabel = displayLabel.Trim();
        OccurrenceCount = occurrenceCount;
        IsExisting = isExisting;
    }

    /// <summary>
    /// Gets the candidate display label.
    /// </summary>
    public string DisplayLabel { get; }

    /// <summary>
    /// Gets the candidate entity identity.
    /// </summary>
    public GraphEntityKey Entity { get; }

    /// <summary>
    /// Gets a value indicating whether the candidate already exists in the target graph.
    /// </summary>
    public bool IsExisting { get; }

    /// <summary>
    /// Gets the number of staged or retained occurrences represented by this candidate.
    /// </summary>
    public int OccurrenceCount { get; }
}
