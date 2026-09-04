namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Defines an optional row-count comparison for an automation notification.
/// </summary>
public enum KustoAutomationRowCountComparison
{
    /// <summary>
    /// Does not compare the current row count with a fixed value.
    /// </summary>
    None,

    /// <summary>
    /// Matches when the current row count equals the configured value.
    /// </summary>
    Equals,

    /// <summary>
    /// Matches when the current row count is greater than or equal to the configured value.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Matches when the current row count is less than or equal to the configured value.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Matches when the current row count does not equal the configured value.
    /// </summary>
    NotEquals,
}
