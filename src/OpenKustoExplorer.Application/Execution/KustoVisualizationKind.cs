namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Identifies a visualization supported by the Kusto render operator.
/// </summary>
public enum KustoVisualizationKind
{
    /// <summary>
    /// Highlights anomalies over time.
    /// </summary>
    AnomalyChart,

    /// <summary>
    /// Displays one or more filled areas.
    /// </summary>
    AreaChart,

    /// <summary>
    /// Displays horizontal bars.
    /// </summary>
    BarChart,

    /// <summary>
    /// Displays scalar values as cards.
    /// </summary>
    Card,

    /// <summary>
    /// Displays vertical columns.
    /// </summary>
    ColumnChart,

    /// <summary>
    /// Displays entities and their directed relationships in the investigation graph.
    /// </summary>
    Graph,

    /// <summary>
    /// Displays values as a ladder chart.
    /// </summary>
    LadderChart,

    /// <summary>
    /// Displays one or more lines.
    /// </summary>
    LineChart,

    /// <summary>
    /// Displays proportional slices.
    /// </summary>
    PieChart,

    /// <summary>
    /// Displays grouped data as a pivot chart.
    /// </summary>
    PivotChart,

    /// <summary>
    /// Displays individual points.
    /// </summary>
    ScatterChart,

    /// <summary>
    /// Displays stacked filled areas.
    /// </summary>
    StackedAreaChart,

    /// <summary>
    /// Displays the result as a table.
    /// </summary>
    Table,

    /// <summary>
    /// Displays values over a datetime axis.
    /// </summary>
    TimeChart,

    /// <summary>
    /// Displays events along an interactive time axis.
    /// </summary>
    TimePivot,

    /// <summary>
    /// Displays hierarchical values as nested rectangles.
    /// </summary>
    TreeMap,
}
