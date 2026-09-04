namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Identifies the value domain used by a chart's x-axis.
/// </summary>
public enum KustoChartAxisKind
{
    /// <summary>
    /// Uses ordered category labels.
    /// </summary>
    Category,

    /// <summary>
    /// Uses continuous numeric values.
    /// </summary>
    Numeric,

    /// <summary>
    /// Uses UTC timestamps.
    /// </summary>
    Time,
}
