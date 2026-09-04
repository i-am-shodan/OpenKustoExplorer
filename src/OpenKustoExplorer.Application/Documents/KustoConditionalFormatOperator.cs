namespace OpenKustoExplorer.Application.Documents;

/// <summary>
/// Identifies a supported conditional-format comparison.
/// </summary>
public enum KustoConditionalFormatOperator
{
    /// <summary>
    /// Matches equal numeric or text values.
    /// </summary>
    Equals,

    /// <summary>
    /// Matches numeric values greater than the comparison value.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Matches numeric values greater than or equal to the comparison value.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Matches numeric values less than the comparison value.
    /// </summary>
    LessThan,

    /// <summary>
    /// Matches numeric values less than or equal to the comparison value.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Matches values in the highest requested percentage of a column.
    /// </summary>
    TopPercent,

    /// <summary>
    /// Matches values in the lowest requested percentage of a column.
    /// </summary>
    BottomPercent,

    /// <summary>
    /// Matches text exactly without case sensitivity.
    /// </summary>
    TextMatches,

    /// <summary>
    /// Matches text beginning with the comparison value.
    /// </summary>
    TextStartsWith,

    /// <summary>
    /// Matches text ending with the comparison value.
    /// </summary>
    TextEndsWith,

    /// <summary>
    /// Matches text containing the comparison value.
    /// </summary>
    TextContains,
}
