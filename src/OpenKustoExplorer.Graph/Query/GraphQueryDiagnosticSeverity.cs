namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Identifies whether an openCypher diagnostic blocks execution.
/// </summary>
public enum GraphQueryDiagnosticSeverity
{
    /// <summary>
    /// Describes a non-blocking query limitation or truncation.
    /// </summary>
    Warning,

    /// <summary>
    /// Describes invalid or unsupported query text that blocks execution.
    /// </summary>
    Error,
}
