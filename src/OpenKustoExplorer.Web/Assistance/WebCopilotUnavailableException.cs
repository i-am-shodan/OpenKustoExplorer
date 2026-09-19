namespace OpenKustoExplorer.Web.Assistance;

/// <summary>
/// Reports a configuration or upstream failure without exposing provider credentials.
/// </summary>
public sealed class WebCopilotUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebCopilotUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The safe user-facing message.</param>
    /// <param name="innerException">The optional underlying provider failure.</param>
    internal WebCopilotUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
