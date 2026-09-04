namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Identifies the final state of one scheduled query execution.
/// </summary>
public enum KustoAutomationRunStatus
{
    /// <summary>
    /// The query completed successfully.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The query failed before producing a usable result.
    /// </summary>
    Failed,

    /// <summary>
    /// The query was canceled during application shutdown or user action.
    /// </summary>
    Canceled,
}
