namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Identifies a table emitted by a graph-to-table export.
/// </summary>
public enum KustoGraphExportTableKind
{
    /// <summary>
    /// Exported graph nodes and their properties.
    /// </summary>
    Nodes,

    /// <summary>
    /// Exported graph edges, endpoint hashes, and their properties.
    /// </summary>
    Edges,
}
