namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Describes one exact, schema-bound value used in a KQL filter predicate.
/// </summary>
public sealed class KustoPredicateInterest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoPredicateInterest"/> class.
    /// </summary>
    /// <param name="columnName">The bound source column name.</param>
    /// <param name="typeName">The normalized Kusto scalar type.</param>
    /// <param name="value">The canonical scalar value.</param>
    /// <param name="literalStart">The literal's zero-based query offset.</param>
    /// <param name="literalLength">The literal source length.</param>
    public KustoPredicateInterest(
        string columnName,
        string typeName,
        string value,
        int literalStart,
        int literalLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(literalStart);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(literalLength);

        ColumnName = columnName;
        TypeName = typeName;
        Value = value;
        LiteralStart = literalStart;
        LiteralLength = literalLength;
    }

    /// <summary>
    /// Gets the bound source column name.
    /// </summary>
    public string ColumnName { get; }

    /// <summary>
    /// Gets the normalized Kusto scalar type.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the canonical scalar value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the literal's zero-based query offset.
    /// </summary>
    public int LiteralStart { get; }

    /// <summary>
    /// Gets the literal source length.
    /// </summary>
    public int LiteralLength { get; }
}
