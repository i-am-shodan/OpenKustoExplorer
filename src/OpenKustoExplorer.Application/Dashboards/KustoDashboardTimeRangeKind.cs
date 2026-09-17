namespace OpenKustoExplorer.Application.Dashboards;

/// <summary>
/// Identifies how a dashboard time range determines its UTC bounds.
/// </summary>
public enum KustoDashboardTimeRangeKind
{
    /// <summary>
    /// Uses a sliding duration ending at the dashboard refresh time.
    /// </summary>
    Relative,

    /// <summary>
    /// Uses fixed UTC start and end times.
    /// </summary>
    Absolute,
}
