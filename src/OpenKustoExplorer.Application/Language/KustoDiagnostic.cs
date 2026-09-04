namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Describes a syntax or semantic issue reported for KQL source text.
/// </summary>
public sealed class KustoDiagnostic
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDiagnostic"/> class.
    /// </summary>
    /// <param name="code">The stable Kusto diagnostic code.</param>
    /// <param name="severity">The diagnostic severity.</param>
    /// <param name="message">The user-facing diagnostic message.</param>
    /// <param name="start">The zero-based source offset.</param>
    /// <param name="length">The number of source characters covered by the diagnostic.</param>
    public KustoDiagnostic(string code, string severity, string message, int start, int length)
    {
        Code = code;
        Severity = severity;
        Message = message;
        Start = start;
        Length = length;
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
    /// Gets the zero-based source offset.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the number of source characters covered by the diagnostic.
    /// </summary>
    public int Length { get; }
}
