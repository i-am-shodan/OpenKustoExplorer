namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Identifies a criterion that triggered an automation notification.
/// </summary>
public enum KustoAutomationNotificationTriggerKind
{
    /// <summary>
    /// The row count changed from the previous successful run.
    /// </summary>
    RowCountChanged,

    /// <summary>
    /// The current row count matched a configured fixed comparison.
    /// </summary>
    RowCountComparison,
}
