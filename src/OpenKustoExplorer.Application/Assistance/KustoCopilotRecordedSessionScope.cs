using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Application.Assistance;

/// <summary>
/// Pins one Copilot conversation and its local tools to a recorded query session.
/// </summary>
public sealed class KustoCopilotRecordedSessionScope
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotRecordedSessionScope"/> class.
    /// </summary>
    /// <param name="sessionId">The recorded session identifier.</param>
    /// <param name="databaseSchema">The optional current schema used for KQL generation and validation.</param>
    public KustoCopilotRecordedSessionScope(Guid sessionId, KustoDatabaseSchema? databaseSchema)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        SessionId = sessionId;
        DatabaseSchema = databaseSchema;
    }

    /// <summary>Gets the recorded session identifier.</summary>
    public Guid SessionId { get; }

    /// <summary>Gets the optional current database schema.</summary>
    public KustoDatabaseSchema? DatabaseSchema { get; }
}
