namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Persists named query-recording sessions and their analyst annotations.
/// </summary>
public interface IKustoRecordedSessionStore
{
    /// <summary>Creates a named session and opens its first recording period.</summary>
    /// <param name="name">The unique session name.</param>
    /// <param name="startedAtUtc">The UTC recording start time.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The opened recording period.</returns>
    public Task<KustoRecordingPeriod> CreateSessionAsync(
        string name,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Opens another recording period in an existing session.</summary>
    /// <param name="sessionId">The session to append to.</param>
    /// <param name="startedAtUtc">The UTC recording start time.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The opened recording period.</returns>
    public Task<KustoRecordingPeriod> AppendSessionAsync(
        Guid sessionId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Stops one open recording period.</summary>
    /// <param name="periodId">The recording period to stop.</param>
    /// <param name="stoppedAtUtc">The UTC stop time.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes after the period is stopped.</returns>
    public Task StopRecordingAsync(
        Guid periodId,
        DateTimeOffset stoppedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Begins one query execution and returns its identifier.</summary>
    /// <param name="execution">The execution metadata and inferred interests.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new execution identifier.</returns>
    public Task<Guid> BeginExecutionAsync(
        KustoRecordedExecutionStart execution,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically finalizes one recorded execution.</summary>
    /// <param name="executionId">The running execution identifier.</param>
    /// <param name="completion">The final outcome and optional result.</param>
    /// <param name="cancellationToken">Cancels and rolls back the operation.</param>
    /// <returns>A task that completes after the execution is finalized.</returns>
    public Task CompleteExecutionAsync(
        Guid executionId,
        KustoRecordedExecutionCompletion completion,
        CancellationToken cancellationToken = default);

    /// <summary>Adds an idempotent pertinent value mark.</summary>
    /// <param name="sessionId">The owning session identifier.</param>
    /// <param name="kind">The cell mark scope.</param>
    /// <param name="coordinate">The marked result coordinate.</param>
    /// <param name="createdAtUtc">The UTC mark time.</param>
    /// <param name="cancellationToken">Cancels and rolls back the operation.</param>
    /// <returns>The existing or newly created mark.</returns>
    public Task<KustoRecordedMark> AddMarkAsync(
        Guid sessionId,
        KustoRecordedMarkKind kind,
        KustoRecordedValueCoordinate coordinate,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Adds idempotent pertinent value marks in one atomic operation.</summary>
    /// <param name="sessionId">The owning session identifier.</param>
    /// <param name="kind">The cell mark scope.</param>
    /// <param name="coordinates">The result coordinates to mark.</param>
    /// <param name="createdAtUtc">The UTC mark time.</param>
    /// <param name="cancellationToken">Cancels and rolls back the operation.</param>
    /// <returns>The existing or newly created marks in coordinate order.</returns>
    public Task<IReadOnlyList<KustoRecordedMark>> AddMarksAsync(
        Guid sessionId,
        KustoRecordedMarkKind kind,
        IReadOnlyList<KustoRecordedValueCoordinate> coordinates,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one user mark.</summary>
    /// <param name="markId">The mark identifier.</param>
    /// <param name="cancellationToken">Cancels and rolls back the operation.</param>
    /// <returns>A task that completes after the mark is removed.</returns>
    public Task RemoveMarkAsync(Guid markId, CancellationToken cancellationToken = default);

    /// <summary>Sets or replaces one chain endpoint.</summary>
    /// <param name="sessionId">The owning session identifier.</param>
    /// <param name="role">The endpoint role to replace.</param>
    /// <param name="coordinate">The selected result coordinate.</param>
    /// <param name="cancellationToken">Cancels and rolls back the operation.</param>
    /// <returns>A task that completes after the endpoint is stored.</returns>
    public Task SetEndpointAsync(
        Guid sessionId,
        KustoChainEndpointRole role,
        KustoRecordedValueCoordinate coordinate,
        CancellationToken cancellationToken = default);

    /// <summary>Clears one chain endpoint.</summary>
    /// <param name="sessionId">The owning session identifier.</param>
    /// <param name="role">The endpoint role to clear.</param>
    /// <param name="cancellationToken">Cancels and rolls back the operation.</param>
    /// <returns>A task that completes after the endpoint is cleared.</returns>
    public Task ClearEndpointAsync(
        Guid sessionId,
        KustoChainEndpointRole role,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the session catalog in most-recently-updated order.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The recorded session summaries.</returns>
    public Task<IReadOnlyList<KustoRecordedSessionSummary>> GetSessionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Gets all value-interest declarations for one session.</summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The session's value interests.</returns>
    public Task<IReadOnlyList<KustoRecordedInterest>> GetInterestsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one complete session aggregate.</summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The session, or <see langword="null"/> when it does not exist.</returns>
    public Task<KustoRecordedSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Renames one recorded execution for display in its session.</summary>
    /// <param name="executionId">The recorded execution identifier.</param>
    /// <param name="displayName">The new display name.</param>
    /// <param name="cancellationToken">Cancels and rolls back the operation.</param>
    /// <returns>A task that completes after the name is stored.</returns>
    public Task RenameExecutionAsync(
        Guid executionId,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes one completed recorded execution and its related content.</summary>
    /// <param name="executionId">The recorded execution identifier.</param>
    /// <param name="cancellationToken">Cancels and rolls back the operation.</param>
    /// <returns>A task that completes after deletion.</returns>
    public Task DeleteExecutionAsync(Guid executionId, CancellationToken cancellationToken = default);

    /// <summary>Deletes one session and all recorded content.</summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancels and rolls back the operation.</param>
    /// <returns>A task that completes after deletion.</returns>
    public Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
