namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Describes whether a materialized result reached the configured record budget.
/// </summary>
public enum KustoQueryResultCompleteness
{
    /// <summary>
    /// The returned result did not exhaust the client record budget.
    /// </summary>
    Complete,

    /// <summary>
    /// The returned result exhausted the client record budget and may omit additional rows.
    /// </summary>
    RecordLimitReached,
}
