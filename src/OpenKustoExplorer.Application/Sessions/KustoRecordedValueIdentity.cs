namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Identifies one exact typed result value.
/// </summary>
public sealed class KustoRecordedValueIdentity : IEquatable<KustoRecordedValueIdentity>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedValueIdentity"/> class.
    /// </summary>
    /// <param name="typeName">The normalized Kusto type.</param>
    /// <param name="canonicalValue">The canonical value payload.</param>
    /// <param name="isNull">Whether the value is null.</param>
    public KustoRecordedValueIdentity(string typeName, string canonicalValue, bool isNull)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(canonicalValue);
        TypeName = typeName.Trim().ToLowerInvariant();
        CanonicalValue = canonicalValue;
        IsNull = isNull;
    }

    /// <summary>Gets the normalized Kusto type.</summary>
    public string TypeName { get; }

    /// <summary>Gets the canonical value payload.</summary>
    public string CanonicalValue { get; }

    /// <summary>Gets a value indicating whether the value is null.</summary>
    public bool IsNull { get; }

    /// <inheritdoc />
    public bool Equals(KustoRecordedValueIdentity? other)
    {
        return other is not null
            && IsNull == other.IsNull
            && string.Equals(TypeName, other.TypeName, StringComparison.Ordinal)
            && string.Equals(CanonicalValue, other.CanonicalValue, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as KustoRecordedValueIdentity);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(TypeName),
            StringComparer.Ordinal.GetHashCode(CanonicalValue),
            IsNull);
    }
}
