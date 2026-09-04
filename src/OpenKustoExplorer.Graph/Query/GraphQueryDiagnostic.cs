namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Describes one position-aware openCypher query issue.
/// </summary>
public sealed class GraphQueryDiagnostic
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQueryDiagnostic"/> class.
    /// </summary>
    /// <param name="severity">Whether this diagnostic blocks execution.</param>
    /// <param name="start">The zero-based query-text start offset.</param>
    /// <param name="length">The affected query-text length.</param>
    /// <param name="message">The analyst-facing diagnostic message.</param>
    public GraphQueryDiagnostic(
        GraphQueryDiagnosticSeverity severity,
        int start,
        int length,
        string message)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Severity = severity;
        Start = start;
        Length = length;
        Message = message.Trim();
    }

    /// <summary>
    /// Gets the diagnostic severity.
    /// </summary>
    public GraphQueryDiagnosticSeverity Severity { get; }

    /// <summary>
    /// Gets the zero-based query-text start offset.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the affected query-text length.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the analyst-facing diagnostic message.
    /// </summary>
    public string Message { get; }
}
