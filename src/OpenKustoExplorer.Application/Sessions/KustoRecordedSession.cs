namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Contains one complete recorded session aggregate.
/// </summary>
public sealed class KustoRecordedSession
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedSession"/> class.
    /// </summary>
    /// <param name="summary">The session summary.</param>
    /// <param name="periods">Recording periods in start order.</param>
    /// <param name="executions">Executions in sequence order.</param>
    /// <param name="interests">Declared value interests.</param>
    /// <param name="marks">User marks.</param>
    /// <param name="endpoints">Selected chain endpoints.</param>
    public KustoRecordedSession(
        KustoRecordedSessionSummary summary,
        IEnumerable<KustoRecordingPeriod> periods,
        IEnumerable<KustoRecordedExecution> executions,
        IEnumerable<KustoRecordedInterest> interests,
        IEnumerable<KustoRecordedMark> marks,
        IEnumerable<KustoChainEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(periods);
        ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(interests);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(endpoints);
        Summary = summary;
        Periods = Array.AsReadOnly(periods.ToArray());
        Executions = Array.AsReadOnly(executions.ToArray());
        Interests = Array.AsReadOnly(interests.ToArray());
        Marks = Array.AsReadOnly(marks.ToArray());
        Endpoints = Array.AsReadOnly(endpoints.ToArray());
    }

    /// <summary>Gets the session summary.</summary>
    public KustoRecordedSessionSummary Summary { get; }

    /// <summary>Gets recording periods in start order.</summary>
    public IReadOnlyList<KustoRecordingPeriod> Periods { get; }

    /// <summary>Gets executions in sequence order.</summary>
    public IReadOnlyList<KustoRecordedExecution> Executions { get; }

    /// <summary>Gets declared value interests.</summary>
    public IReadOnlyList<KustoRecordedInterest> Interests { get; }

    /// <summary>Gets user marks.</summary>
    public IReadOnlyList<KustoRecordedMark> Marks { get; }

    /// <summary>Gets selected chain endpoints.</summary>
    public IReadOnlyList<KustoChainEndpoint> Endpoints { get; }
}
