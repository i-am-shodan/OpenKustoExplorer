namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Defines one local comparison applied to a materialized result column.
/// </summary>
public enum KustoResultFilterOperator
{
    /// <summary>
    /// Matches values equal to the filter text.
    /// </summary>
    Equals,

    /// <summary>
    /// Matches values different from the filter text.
    /// </summary>
    NotEquals,

    /// <summary>
    /// Matches values containing the filter text.
    /// </summary>
    Contains,

    /// <summary>
    /// Matches values that do not contain the filter text.
    /// </summary>
    DoesNotContain,

    /// <summary>
    /// Matches values beginning with the filter text.
    /// </summary>
    StartsWith,

    /// <summary>
    /// Matches values ending with the filter text.
    /// </summary>
    EndsWith,

    /// <summary>
    /// Matches values greater than the filter value.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Matches values greater than or equal to the filter value.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Matches values less than the filter value.
    /// </summary>
    LessThan,

    /// <summary>
    /// Matches values less than or equal to the filter value.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Matches empty values.
    /// </summary>
    IsEmpty,

    /// <summary>
    /// Matches nonempty values.
    /// </summary>
    IsNotEmpty,
}
