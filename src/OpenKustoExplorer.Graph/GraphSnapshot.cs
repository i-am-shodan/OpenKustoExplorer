namespace OpenKustoExplorer.Graph;

/// <summary>
/// Identifies one immutable current-generation view of a named graph.
/// </summary>
public readonly struct GraphSnapshot : IEquatable<GraphSnapshot>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphSnapshot"/> struct.
    /// </summary>
    /// <param name="graphId">The named graph identifier.</param>
    /// <param name="generationId">The graph's current generation identifier.</param>
    public GraphSnapshot(Guid graphId, Guid generationId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(generationId, Guid.Empty);
        GraphId = graphId;
        GenerationId = generationId;
    }

    /// <summary>
    /// Gets the named graph identifier.
    /// </summary>
    public Guid GraphId { get; }

    /// <summary>
    /// Gets the graph generation identifier.
    /// </summary>
    public Guid GenerationId { get; }

    /// <summary>
    /// Determines whether two snapshots identify the same graph generation.
    /// </summary>
    /// <param name="left">The first snapshot.</param>
    /// <param name="right">The second snapshot.</param>
    /// <returns><see langword="true"/> when both identities match.</returns>
    public static bool operator ==(GraphSnapshot left, GraphSnapshot right) => left.Equals(right);

    /// <summary>
    /// Determines whether two snapshots identify different graph generations.
    /// </summary>
    /// <param name="left">The first snapshot.</param>
    /// <param name="right">The second snapshot.</param>
    /// <returns><see langword="true"/> when either identity differs.</returns>
    public static bool operator !=(GraphSnapshot left, GraphSnapshot right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(GraphSnapshot other) => GraphId == other.GraphId && GenerationId == other.GenerationId;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GraphSnapshot other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GraphId, GenerationId);

    /// <inheritdoc />
    public override string ToString() => $"{GraphId:D}/{GenerationId:D}";
}
