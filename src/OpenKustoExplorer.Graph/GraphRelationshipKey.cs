namespace OpenKustoExplorer.Graph;

/// <summary>
/// Identifies one directed relationship while preserving true parallel edges through an optional discriminator.
/// </summary>
public readonly struct GraphRelationshipKey : IEquatable<GraphRelationshipKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphRelationshipKey"/> struct.
    /// </summary>
    /// <param name="source">The source entity.</param>
    /// <param name="target">The target entity.</param>
    /// <param name="typeName">The relationship type or label.</param>
    /// <param name="discriminator">An optional mapped edge identifier for parallel relationships.</param>
    public GraphRelationshipKey(
        GraphEntityKey source,
        GraphEntityKey target,
        string typeName,
        string discriminator = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(discriminator);

        Source = source;
        Target = target;
        TypeName = typeName.Trim();
        Discriminator = discriminator.Trim();
    }

    /// <summary>
    /// Gets the source entity.
    /// </summary>
    public GraphEntityKey Source { get; }

    /// <summary>
    /// Gets the target entity.
    /// </summary>
    public GraphEntityKey Target { get; }

    /// <summary>
    /// Gets the relationship type or label.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the optional mapped edge identifier used to distinguish parallel relationships.
    /// </summary>
    public string Discriminator { get; }

    /// <summary>
    /// Determines whether two relationship keys identify the same relationship.
    /// </summary>
    /// <param name="left">The first relationship key.</param>
    /// <param name="right">The second relationship key.</param>
    /// <returns><see langword="true"/> when the keys are equal.</returns>
    public static bool operator ==(GraphRelationshipKey left, GraphRelationshipKey right) => left.Equals(right);

    /// <summary>
    /// Determines whether two relationship keys identify different relationships.
    /// </summary>
    /// <param name="left">The first relationship key.</param>
    /// <param name="right">The second relationship key.</param>
    /// <returns><see langword="true"/> when the keys are not equal.</returns>
    public static bool operator !=(GraphRelationshipKey left, GraphRelationshipKey right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(GraphRelationshipKey other)
    {
        return Source.Equals(other.Source)
            && Target.Equals(other.Target)
            && string.Equals(TypeName, other.TypeName, StringComparison.Ordinal)
            && string.Equals(Discriminator, other.Discriminator, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GraphRelationshipKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hashCode = default;
        hashCode.Add(Source);
        hashCode.Add(Target);
        hashCode.Add(TypeName, StringComparer.Ordinal);
        hashCode.Add(Discriminator, StringComparer.Ordinal);
        return hashCode.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        string relationship = $"{Source}-[{TypeName}]->{Target}";
        return Discriminator.Length == 0 ? relationship : $"{relationship}#{Discriminator}";
    }
}
