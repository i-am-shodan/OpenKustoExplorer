namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Identifies the lifecycle state of a recorded query execution.
/// </summary>
public enum KustoRecordedExecutionStatus
{
    /// <summary>The query has started but has not completed.</summary>
    Running,

    /// <summary>The query completed successfully.</summary>
    Succeeded,

    /// <summary>The query failed.</summary>
    Failed,

    /// <summary>The query was canceled.</summary>
    Canceled,

    /// <summary>The application stopped before the query could be finalized.</summary>
    Interrupted,

    /// <summary>The query completed but its recording could not be finalized.</summary>
    CaptureFailed,
}
