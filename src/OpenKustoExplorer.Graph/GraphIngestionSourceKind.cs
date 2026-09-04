namespace OpenKustoExplorer.Graph;

/// <summary>
/// Identifies the workflow that produced a graph ingestion.
/// </summary>
public enum GraphIngestionSourceKind
{
    /// <summary>
    /// A query explicitly run by the analyst.
    /// </summary>
    ManualQuery,

    /// <summary>
    /// A persisted scheduled query automation.
    /// </summary>
    Automation,
}
