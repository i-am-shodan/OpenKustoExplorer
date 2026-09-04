using System.Globalization;

namespace OpenKustoExplorer.Graph;

/// <summary>
/// Identifies one graph entity by semantic type, canonical identifier, and optional source boundary.
/// </summary>
public readonly struct GraphEntityKey : IEquatable<GraphEntityKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphEntityKey"/> struct.
    /// </summary>
    /// <param name="kind">The broad semantic entity category.</param>
    /// <param name="typeName">The canonical or source-provided type label.</param>
    /// <param name="canonicalId">The normalized entity identifier.</param>
    /// <param name="sourceNamespace">An optional source boundary used until global identity is approved.</param>
    public GraphEntityKey(
        GraphEntityKind kind,
        string typeName,
        string canonicalId,
        string sourceNamespace = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalId);
        ArgumentNullException.ThrowIfNull(sourceNamespace);

        Kind = kind;
        TypeName = typeName.Trim();
        CanonicalId = canonicalId.Trim();
        SourceNamespace = sourceNamespace.Trim();
    }

    /// <summary>
    /// Gets the broad semantic entity category.
    /// </summary>
    public GraphEntityKind Kind { get; }

    /// <summary>
    /// Gets the canonical or source-provided type label.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the normalized entity identifier.
    /// </summary>
    public string CanonicalId { get; }

    /// <summary>
    /// Gets the source boundary, or an empty string for an approved global identity.
    /// </summary>
    public string SourceNamespace { get; }

    /// <summary>
    /// Gets a value indicating whether this identity is isolated to a source boundary.
    /// </summary>
    public bool IsSourceScoped => SourceNamespace.Length > 0;

    /// <summary>
    /// Determines whether two entity keys identify the same entity.
    /// </summary>
    /// <param name="left">The first entity key.</param>
    /// <param name="right">The second entity key.</param>
    /// <returns><see langword="true"/> when the keys are equal.</returns>
    public static bool operator ==(GraphEntityKey left, GraphEntityKey right) => left.Equals(right);

    /// <summary>
    /// Determines whether two entity keys identify different entities.
    /// </summary>
    /// <param name="left">The first entity key.</param>
    /// <param name="right">The second entity key.</param>
    /// <returns><see langword="true"/> when the keys are not equal.</returns>
    public static bool operator !=(GraphEntityKey left, GraphEntityKey right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(GraphEntityKey other)
    {
        return Kind == other.Kind
            && string.Equals(TypeName, other.TypeName, StringComparison.Ordinal)
            && string.Equals(CanonicalId, other.CanonicalId, StringComparison.Ordinal)
            && string.Equals(SourceNamespace, other.SourceNamespace, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GraphEntityKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hashCode = default;
        hashCode.Add(Kind);
        hashCode.Add(TypeName, StringComparer.Ordinal);
        hashCode.Add(CanonicalId, StringComparer.Ordinal);
        hashCode.Add(SourceNamespace, StringComparer.Ordinal);
        return hashCode.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        string identity = string.Create(
            CultureInfo.InvariantCulture,
            $"{TypeName}:{CanonicalId}");
        return IsSourceScoped
            ? string.Create(CultureInfo.InvariantCulture, $"{SourceNamespace}/{identity}")
            : identity;
    }
}
