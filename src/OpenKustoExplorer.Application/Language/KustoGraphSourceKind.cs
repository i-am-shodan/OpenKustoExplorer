namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Identifies the Kusto expression that produces a terminal graph value.
/// </summary>
public enum KustoGraphSourceKind
{
    /// <summary>
    /// A transient graph produced by the make-graph operator.
    /// </summary>
    MakeGraph,

    /// <summary>
    /// A persistent or transient graph referenced by the graph function.
    /// </summary>
    GraphFunction,
}
