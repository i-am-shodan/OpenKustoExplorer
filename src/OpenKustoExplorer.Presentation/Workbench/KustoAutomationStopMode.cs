namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Identifies how a scheduled query's lifetime is limited.
/// </summary>
public enum KustoAutomationStopMode
{
    /// <summary>
    /// Runs until manually paused or deleted.
    /// </summary>
    Never,

    /// <summary>
    /// Stops after a configured number of hours.
    /// </summary>
    Hours,

    /// <summary>
    /// Stops after a configured number of days.
    /// </summary>
    Days,
}
