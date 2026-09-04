namespace OpenKustoExplorer.Application.Assistance;

/// <summary>
/// Contains assistant display text and an optional complete KQL proposal.
/// </summary>
public sealed class KustoCopilotReply
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotReply"/> class.
    /// </summary>
    /// <param name="message">The concise user-facing response.</param>
    /// <param name="proposedQuery">An optional complete replacement query document.</param>
    public KustoCopilotReply(string message, string? proposedQuery)
        : this(message, proposedQuery, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotReply"/> class.
    /// </summary>
    /// <param name="message">The concise user-facing response.</param>
    /// <param name="proposedQuery">An optional complete replacement KQL document.</param>
    /// <param name="proposedCypher">An optional complete read-only openCypher query.</param>
    public KustoCopilotReply(string message, string? proposedQuery, string? proposedCypher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Message = message.Trim();
        ProposedQuery = string.IsNullOrWhiteSpace(proposedQuery) ? null : proposedQuery.Trim();
        ProposedCypher = string.IsNullOrWhiteSpace(proposedCypher) ? null : proposedCypher.Trim();
    }

    /// <summary>
    /// Gets the concise user-facing response.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets an optional complete replacement query document.
    /// </summary>
    public string? ProposedQuery { get; }

    /// <summary>
    /// Gets an optional complete read-only openCypher proposal.
    /// </summary>
    public string? ProposedCypher { get; }
}
