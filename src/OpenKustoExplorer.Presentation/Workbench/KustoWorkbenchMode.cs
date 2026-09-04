namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Identifies the active top-level workbench destination.
/// </summary>
public enum KustoWorkbenchMode
{
    /// <summary>
    /// Query documents, schema Explorer, results, and visualizations.
    /// </summary>
    Query,

    /// <summary>
    /// Query-backed operational dashboards.
    /// </summary>
    Dashboards,

    /// <summary>
    /// Scheduled query automations and run history.
    /// </summary>
    Automations,

    /// <summary>
    /// The app-wide investigation graph.
    /// </summary>
    Graph,
}
