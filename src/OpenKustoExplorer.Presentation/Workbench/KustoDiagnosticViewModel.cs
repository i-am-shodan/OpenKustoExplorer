using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one KQL diagnostic in the workbench problems list.
/// </summary>
public sealed class KustoDiagnosticViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDiagnosticViewModel"/> class.
    /// </summary>
    /// <param name="diagnostic">The immutable application diagnostic.</param>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostic"/> is <see langword="null"/>.</exception>
    public KustoDiagnosticViewModel(KustoDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        Code = diagnostic.Code;
        Severity = diagnostic.Severity;
        Message = diagnostic.Message;
        PositionText = $"{diagnostic.Start + 1}";
    }

    /// <summary>
    /// Gets the stable Kusto diagnostic code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the diagnostic severity.
    /// </summary>
    public string Severity { get; }

    /// <summary>
    /// Gets the user-facing diagnostic message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the one-based source position text.
    /// </summary>
    public string PositionText { get; }
}
