namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Describes one recording interval within a named session.
/// </summary>
public sealed class KustoRecordingPeriod
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordingPeriod"/> class.
    /// </summary>
    /// <param name="id">The recording-period identifier.</param>
    /// <param name="sessionId">The owning session identifier.</param>
    /// <param name="startedAtUtc">The UTC start time.</param>
    /// <param name="stoppedAtUtc">The optional UTC stop time.</param>
    public KustoRecordingPeriod(Guid id, Guid sessionId, DateTimeOffset startedAtUtc, DateTimeOffset? stoppedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        if (stoppedAtUtc < startedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(stoppedAtUtc));
        }

        Id = id;
        SessionId = sessionId;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        StoppedAtUtc = stoppedAtUtc?.ToUniversalTime();
    }

    /// <summary>Gets the recording-period identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the owning session identifier.</summary>
    public Guid SessionId { get; }

    /// <summary>Gets the UTC start time.</summary>
    public DateTimeOffset StartedAtUtc { get; }

    /// <summary>Gets the optional UTC stop time.</summary>
    public DateTimeOffset? StoppedAtUtc { get; }
}
