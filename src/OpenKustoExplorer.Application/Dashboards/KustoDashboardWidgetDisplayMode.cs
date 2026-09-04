namespace OpenKustoExplorer.Application.Dashboards;

/// <summary>
/// Identifies how a dashboard widget presents query results.
/// </summary>
public enum KustoDashboardWidgetDisplayMode
{
    /// <summary>
    /// Displays result rows in a table.
    /// </summary>
    Table,

    /// <summary>
    /// Displays results using a Kusto visualization.
    /// </summary>
    Visualization,
}
