namespace OpenKustoExplorer.Graph;

/// <summary>
/// Pins a graph mutation to the generation that was current when work began.
/// </summary>
public readonly struct GraphWriteTarget : IEquatable<GraphWriteTarget>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphWriteTarget"/> struct.
    /// </summary>
    /// <param name="graphId">The target named graph.</param>
    /// <param name="expectedGenerationId">The generation expected to remain current.</param>
    public GraphWriteTarget(Guid graphId, Guid expectedGenerationId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(expectedGenerationId, Guid.Empty);
        GraphId = graphId;
        ExpectedGenerationId = expectedGenerationId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphWriteTarget"/> struct.
    /// </summary>
    /// <param name="snapshot">The snapshot being mutated.</param>
    public GraphWriteTarget(GraphSnapshot snapshot)
        : this(snapshot.GraphId, snapshot.GenerationId)
    {
    }

    /// <summary>
    /// Gets the target named graph.
    /// </summary>
    public Guid GraphId { get; }

    /// <summary>
    /// Gets the generation expected to remain current.
    /// </summary>
    public Guid ExpectedGenerationId { get; }

    /// <summary>
    /// Gets the immutable snapshot represented by this target.
    /// </summary>
    public GraphSnapshot Snapshot => new(GraphId, ExpectedGenerationId);

    /// <summary>
    /// Determines whether two write targets are equal.
    /// </summary>
    /// <param name="left">The first target.</param>
    /// <param name="right">The second target.</param>
    /// <returns><see langword="true"/> when both identities match.</returns>
    public static bool operator ==(GraphWriteTarget left, GraphWriteTarget right) => left.Equals(right);

    /// <summary>
    /// Determines whether two write targets differ.
    /// </summary>
    /// <param name="left">The first target.</param>
    /// <param name="right">The second target.</param>
    /// <returns><see langword="true"/> when either identity differs.</returns>
    public static bool operator !=(GraphWriteTarget left, GraphWriteTarget right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(GraphWriteTarget other)
    {
        return GraphId == other.GraphId && ExpectedGenerationId == other.ExpectedGenerationId;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GraphWriteTarget other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GraphId, ExpectedGenerationId);
}
