using OpenKustoExplorer.Graph.Query;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one position-aware openCypher diagnostic.
/// </summary>
public sealed class GraphQueryDiagnosticViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQueryDiagnosticViewModel"/> class.
    /// </summary>
    /// <param name="diagnostic">The immutable query diagnostic.</param>
    public GraphQueryDiagnosticViewModel(GraphQueryDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        Message = diagnostic.Message;
        Severity = diagnostic.Severity.ToString();
        LocationText = diagnostic.Length == 0
            ? $"Offset {diagnostic.Start:N0}"
            : $"Offset {diagnostic.Start:N0}, length {diagnostic.Length:N0}";
    }

    /// <summary>
    /// Gets the diagnostic message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the diagnostic severity text.
    /// </summary>
    public string Severity { get; }

    /// <summary>
    /// Gets the affected source location text.
    /// </summary>
    public string LocationText { get; }
}
