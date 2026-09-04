namespace OpenKustoExplorer.Application.Documents;

/// <summary>
/// Describes one persisted result-table conditional-formatting rule.
/// </summary>
public sealed class KustoConditionalFormatRule
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoConditionalFormatRule"/> class.
    /// </summary>
    /// <param name="id">The stable rule identifier.</param>
    /// <param name="columnName">The result column evaluated by the rule.</param>
    /// <param name="comparison">The comparison operator.</param>
    /// <param name="comparisonValue">The comparison value or percentile.</param>
    /// <param name="target">The formatting target.</param>
    /// <param name="colorHex">The opaque RGB color in <c>#RRGGBB</c> form.</param>
    public KustoConditionalFormatRule(
        Guid id,
        string columnName,
        KustoConditionalFormatOperator comparison,
        string comparisonValue,
        KustoConditionalFormatTarget target,
        string colorHex)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentNullException.ThrowIfNull(comparisonValue);

        if (!Enum.IsDefined(comparison))
        {
            throw new ArgumentOutOfRangeException(nameof(comparison));
        }

        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        if (!IsColorHex(colorHex))
        {
            throw new ArgumentException("A conditional-format color must use #RRGGBB format.", nameof(colorHex));
        }

        Id = id;
        ColumnName = columnName.Trim();
        Comparison = comparison;
        ComparisonValue = comparisonValue.Trim();
        Target = target;
        ColorHex = colorHex.ToUpperInvariant();
    }

    /// <summary>
    /// Gets the stable rule identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the result column evaluated by the rule.
    /// </summary>
    public string ColumnName { get; }

    /// <summary>
    /// Gets the comparison operator.
    /// </summary>
    public KustoConditionalFormatOperator Comparison { get; }

    /// <summary>
    /// Gets the comparison value or percentile.
    /// </summary>
    public string ComparisonValue { get; }

    /// <summary>
    /// Gets the formatting target.
    /// </summary>
    public KustoConditionalFormatTarget Target { get; }

    /// <summary>
    /// Gets the opaque RGB color in <c>#RRGGBB</c> form.
    /// </summary>
    public string ColorHex { get; }

    private static bool IsColorHex(string? colorHex)
    {
        bool valid = colorHex?.Length == 7 && colorHex[0] == '#';

        for (int index = 1; valid && index < colorHex!.Length; index++)
        {
            valid = Uri.IsHexDigit(colorHex[index]);
        }

        return valid;
    }
}
