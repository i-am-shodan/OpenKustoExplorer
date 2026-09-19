using System.Text;
using System.Text.Json;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Portable.Sessions;

/// <summary>
/// Persists recorded sessions as one versioned JSON snapshot using host-provided storage.
/// </summary>
public sealed class JsonKustoRecordedSessionStore : IKustoRecordedSessionArchiveStore, IDisposable
{
    /// <summary>Gets the maximum total rows persisted for one recorded execution.</summary>
    public const int MaximumRecordedRowsPerExecution = 500;

    /// <summary>Gets the maximum estimated result payload persisted for one execution.</summary>
    public const int MaximumRecordedResultBytesPerExecution = 2 * 1024 * 1024;

    /// <summary>Gets the maximum query text persisted for one execution.</summary>
    public const int MaximumRecordedQueryBytes = 256 * 1024;

    /// <summary>Gets the maximum number of retained sessions.</summary>
    public const int MaximumSessionCount = 100;

    /// <summary>Gets the maximum number of executions retained by one session.</summary>
    public const int MaximumExecutionsPerSession = 500;

    /// <summary>Gets the maximum JSON snapshot size used by recorded sessions.</summary>
    public const long MaximumSnapshotBytes = 1024L * 1024 * 1024;

    private const int CompletionMetadataReserveBytes = 64 * 1024;
    private readonly int maximumExecutionsPerSession;
    private readonly int maximumResultBytesPerExecution;
    private readonly int maximumSessionCount;
    private readonly long maximumSnapshotBytes;
    private readonly IKustoRecordedSessionSnapshotStore snapshotStore;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private KustoRecordedSessionSnapshotDocument snapshot;
    private bool isDisposed;

    private JsonKustoRecordedSessionStore(
        IKustoRecordedSessionSnapshotStore snapshotStore,
        KustoRecordedSessionSnapshotDocument snapshot,
        TimeProvider timeProvider,
        int maximumSessionCount,
        int maximumExecutionsPerSession,
        long maximumSnapshotBytes,
        int maximumResultBytesPerExecution)
    {
        this.snapshotStore = snapshotStore;
        this.snapshot = snapshot;
        this.timeProvider = timeProvider;
        this.maximumSessionCount = maximumSessionCount;
        this.maximumExecutionsPerSession = maximumExecutionsPerSession;
        this.maximumSnapshotBytes = maximumSnapshotBytes;
        this.maximumResultBytesPerExecution = maximumResultBytesPerExecution;
    }

    /// <summary>
    /// Loads a portable recorded-session store and recovers interrupted recordings.
    /// </summary>
    /// <param name="snapshotStore">The host-specific durable snapshot store.</param>
    /// <param name="timeProvider">The clock used for recovery and user-initiated metadata changes.</param>
    /// <param name="invalidSnapshotHandler">Optionally preserves an invalid snapshot before recovery.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The initialized recorded-session store.</returns>
    public static async Task<JsonKustoRecordedSessionStore> CreateAsync(
        IKustoRecordedSessionSnapshotStore snapshotStore,
        TimeProvider? timeProvider = null,
        Func<string, CancellationToken, Task>? invalidSnapshotHandler = null,
        CancellationToken cancellationToken = default)
    {
        return await CreateAsync(
            snapshotStore,
            timeProvider ?? TimeProvider.System,
            MaximumSessionCount,
            MaximumExecutionsPerSession,
            MaximumSnapshotBytes,
            MaximumRecordedResultBytesPerExecution,
            invalidSnapshotHandler,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a portable recorded-session store with explicit retention limits.
    /// </summary>
    /// <param name="snapshotStore">The host-specific durable snapshot store.</param>
    /// <param name="timeProvider">The clock used for recovery and user-initiated metadata changes.</param>
    /// <param name="maximumSessionCount">The maximum retained sessions.</param>
    /// <param name="maximumExecutionsPerSession">The maximum executions retained in one session.</param>
    /// <param name="maximumSnapshotBytes">The maximum serialized snapshot size.</param>
    /// <param name="maximumResultBytesPerExecution">The maximum estimated result bytes per execution.</param>
    /// <param name="invalidSnapshotHandler">Optionally preserves an invalid snapshot before recovery.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The initialized recorded-session store.</returns>
    public static async Task<JsonKustoRecordedSessionStore> CreateAsync(
        IKustoRecordedSessionSnapshotStore snapshotStore,
        TimeProvider timeProvider,
        int maximumSessionCount,
        int maximumExecutionsPerSession,
        long maximumSnapshotBytes,
        int maximumResultBytesPerExecution,
        Func<string, CancellationToken, Task>? invalidSnapshotHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSessionCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExecutionsPerSession);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSnapshotBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResultBytesPerExecution);

        string? json = await snapshotStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        KustoRecordedSessionSnapshotDocument initialSnapshot = DeserializeOrCreate(
            json,
            out bool invalidSnapshot);
        if (invalidSnapshot && invalidSnapshotHandler is not null)
        {
            await invalidSnapshotHandler(json!, cancellationToken).ConfigureAwait(false);
        }

        JsonKustoRecordedSessionStore store = new(
            snapshotStore,
            initialSnapshot,
            timeProvider,
            maximumSessionCount,
            maximumExecutionsPerSession,
            maximumSnapshotBytes,
            maximumResultBytesPerExecution);
        await store.RecoverInterruptedRecordingsAsync(cancellationToken).ConfigureAwait(false);
        return store;
    }

    /// <inheritdoc />
    public Task<KustoRecordingPeriod> CreateSessionAsync(
        string name,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalizedName = name.Trim();
        DateTimeOffset utcStart = startedAtUtc.ToUniversalTime();
        return ExecuteWriteAsync(
            document =>
            {
                EnsureNoOpenPeriod(document);
                if (document.Sessions.Count >= maximumSessionCount)
                {
                    throw new InvalidOperationException(
                        $"Recorded session storage contains {document.Sessions.Count:N0} sessions. "
                        + "Delete a session before creating another.");
                }

                if (document.Sessions.Any(session => string.Equals(
                    session.Name,
                    normalizedName,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("A recorded session with this name already exists.");
                }

                Guid sessionId = Guid.NewGuid();
                Guid periodId = Guid.NewGuid();
                document.Sessions.Add(new KustoRecordedSessionDocument
                {
                    Id = sessionId,
                    Name = normalizedName,
                    CreatedAtUtc = utcStart,
                    LastUpdatedAtUtc = utcStart,
                    Periods =
                    [
                        new KustoRecordingPeriodDocument
                        {
                            Id = periodId,
                            SessionId = sessionId,
                            StartedAtUtc = utcStart,
                        },
                    ],
                });
                return new KustoRecordingPeriod(periodId, sessionId, utcStart, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<KustoRecordingPeriod> AppendSessionAsync(
        Guid sessionId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        DateTimeOffset utcStart = startedAtUtc.ToUniversalTime();
        return ExecuteWriteAsync(
            document =>
            {
                KustoRecordedSessionDocument session = GetSession(document, sessionId);
                EnsureNoOpenPeriod(document);
                Guid periodId = Guid.NewGuid();
                session.Periods.Add(new KustoRecordingPeriodDocument
                {
                    Id = periodId,
                    SessionId = sessionId,
                    StartedAtUtc = utcStart,
                });
                session.LastUpdatedAtUtc = utcStart;
                return new KustoRecordingPeriod(periodId, sessionId, utcStart, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task StopRecordingAsync(
        Guid periodId,
        DateTimeOffset stoppedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(periodId, Guid.Empty);
        DateTimeOffset utcStop = stoppedAtUtc.ToUniversalTime();
        return ExecuteWriteAsync(
            document =>
            {
                KustoRecordedSessionDocument? session = document.Sessions.FirstOrDefault(
                    candidate => candidate.Periods.Any(period => period.Id == periodId));
                KustoRecordingPeriodDocument? period = session?.Periods.FirstOrDefault(
                    candidate => candidate.Id == periodId && candidate.StoppedAtUtc is null);
                if (session is null || period is null)
                {
                    throw new InvalidOperationException(
                        "The recording period does not exist or is already stopped.");
                }

                if (utcStop < period.StartedAtUtc)
                {
                    throw new InvalidOperationException(
                        "The recording stop time cannot precede its start time.");
                }

                period.StoppedAtUtc = utcStop;
                session.LastUpdatedAtUtc = utcStop;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task PauseRecordingAsync(
        Guid periodId,
        DateTimeOffset pausedAtUtc,
        IReadOnlyCollection<Guid> discardedExecutionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(periodId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(discardedExecutionIds);
        Guid[] executionIds = discardedExecutionIds.Distinct().ToArray();
        if (executionIds.Any(executionId => executionId == Guid.Empty))
        {
            throw new ArgumentException("Discarded execution identifiers cannot be empty.", nameof(discardedExecutionIds));
        }

        DateTimeOffset utcPause = pausedAtUtc.ToUniversalTime();
        return ExecuteWriteAsync(
            document =>
            {
                KustoRecordedSessionDocument? session = document.Sessions.FirstOrDefault(
                    candidate => candidate.Periods.Any(period =>
                        period.Id == periodId && period.StoppedAtUtc is null));
                KustoRecordingPeriodDocument? period = session?.Periods.FirstOrDefault(
                    candidate => candidate.Id == periodId && candidate.StoppedAtUtc is null);
                if (session is null || period is null)
                {
                    throw new InvalidOperationException(
                        "The recording period does not exist or is already stopped.");
                }

                if (utcPause < period.StartedAtUtc)
                {
                    throw new InvalidOperationException(
                        "The recording pause time cannot precede its start time.");
                }

                HashSet<Guid> discardedIds = session.Executions
                    .Where(execution => execution.PeriodId == periodId && executionIds.Contains(execution.Id))
                    .Select(execution => execution.Id)
                    .ToHashSet();
                session.Executions.RemoveAll(execution => discardedIds.Contains(execution.Id));
                session.Interests.RemoveAll(interest =>
                    (interest.DeclaredExecutionId is Guid declaredExecutionId && discardedIds.Contains(declaredExecutionId))
                    || (interest.Coordinate is not null && discardedIds.Contains(interest.Coordinate.ExecutionId)));
                session.Marks.RemoveAll(mark => discardedIds.Contains(mark.Coordinate.ExecutionId));
                session.Endpoints.RemoveAll(endpoint => discardedIds.Contains(endpoint.Coordinate.ExecutionId));
                period.StoppedAtUtc = utcPause;
                session.LastUpdatedAtUtc = utcPause;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Guid> BeginExecutionAsync(
        KustoRecordedExecutionStart execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (Encoding.UTF8.GetByteCount(execution.Request.QueryText) > MaximumRecordedQueryBytes)
        {
            throw new InvalidOperationException(
                $"Recorded query text exceeds the {MaximumRecordedQueryBytes / 1024:N0} KiB limit.");
        }

        return ExecuteWriteAsync(
            document =>
            {
                KustoRecordedSessionDocument? session = document.Sessions.FirstOrDefault(
                    candidate => candidate.Periods.Any(period =>
                        period.Id == execution.PeriodId && period.StoppedAtUtc is null));
                if (session is null)
                {
                    throw new InvalidOperationException(
                        "The recording period does not exist or is already stopped.");
                }

                if (session.Executions.Count >= maximumExecutionsPerSession)
                {
                    throw new InvalidOperationException(
                        $"This session contains {session.Executions.Count:N0} recorded queries. "
                        + "Delete a query or start another session.");
                }

                Guid executionId = Guid.NewGuid();
                long sequence = session.Executions.Count == 0
                    ? 1
                    : session.Executions.Max(candidate => candidate.Sequence) + 1;
                session.Executions.Add(CreateExecutionDocument(executionId, session.Id, sequence, execution));
                foreach (KustoPredicateInterest interest in execution.PredicateInterests)
                {
                    KustoRecordedValueIdentity identity = new(interest.TypeName, interest.Value, false);
                    session.Interests.Add(new KustoRecordedInterestDocument
                    {
                        Id = Guid.NewGuid(),
                        SessionId = session.Id,
                        DeclaredExecutionId = executionId,
                        Source = KustoRecordedInterestSource.QueryPredicate,
                        ColumnName = interest.ColumnName,
                        TypeName = identity.TypeName,
                        CanonicalValue = identity.CanonicalValue,
                        IsNull = identity.IsNull,
                        LiteralStart = interest.LiteralStart,
                        LiteralLength = interest.LiteralLength,
                    });
                }

                session.LastUpdatedAtUtc = execution.StartedAtUtc;
                return executionId;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task CompleteExecutionAsync(
        Guid executionId,
        KustoRecordedExecutionCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(completion);
        return ExecuteWriteAsync(
            document =>
            {
                (KustoRecordedSessionDocument Session, KustoRecordedExecutionDocument Execution) found =
                    FindExecution(document, executionId);
                if (found.Execution.Status != KustoRecordedExecutionStatus.Running)
                {
                    throw new InvalidOperationException("The recorded execution is not running.");
                }

                long usedBytes = Encoding.UTF8.GetByteCount(KustoRecordedSessionSnapshotJson.Serialize(document));
                long availableBytes = Math.Min(
                    maximumResultBytesPerExecution,
                    Math.Max(0, maximumSnapshotBytes - usedBytes - CompletionMetadataReserveBytes));
                KustoQueryResult? result = completion.Result is null
                    ? null
                    : KustoRecordedResultLimiter.Limit(
                        completion.Result,
                        MaximumRecordedRowsPerExecution,
                        availableBytes);
                found.Execution.CompletedAtUtc = completion.CompletedAtUtc;
                found.Execution.Status = completion.Status;
                found.Execution.ErrorMessage = completion.ErrorMessage;
                found.Execution.Result = result is null ? null : CreateResultDocument(result);
                found.Session.LastUpdatedAtUtc = completion.CompletedAtUtc;
                if (result is not null)
                {
                    AddConfirmedPredicateMarks(found.Session, found.Execution, completion.CompletedAtUtc);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<KustoRecordedMark> AddMarkAsync(
        Guid sessionId,
        KustoRecordedMarkKind kind,
        KustoRecordedValueCoordinate coordinate,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        IReadOnlyList<KustoRecordedMark> marks = await AddMarksAsync(
            sessionId,
            kind,
            [coordinate],
            createdAtUtc,
            cancellationToken).ConfigureAwait(false);
        return marks[0];
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KustoRecordedMark>> AddMarksAsync(
        Guid sessionId,
        KustoRecordedMarkKind kind,
        IReadOnlyList<KustoRecordedValueCoordinate> coordinates,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(coordinates);
        if (kind != KustoRecordedMarkKind.Cell)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        KustoRecordedValueCoordinate[] distinctCoordinates = coordinates
            .DistinctBy(ToCoordinateKey)
            .ToArray();
        return ExecuteWriteAsync<IReadOnlyList<KustoRecordedMark>>(
            document =>
            {
                KustoRecordedSessionDocument session = GetSession(document, sessionId);
                foreach (KustoRecordedValueCoordinate coordinate in distinctCoordinates)
                {
                    _ = GetCoordinateValue(session, coordinate);
                }

                return distinctCoordinates
                    .Select(coordinate => AddMark(
                        session,
                        kind,
                        coordinate,
                        createdAtUtc,
                        KustoRecordedInterestSource.ManualCell))
                    .ToArray();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveMarkAsync(Guid markId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(markId, Guid.Empty);
        return ExecuteWriteAsync(
            document =>
            {
                foreach (KustoRecordedSessionDocument session in document.Sessions)
                {
                    session.Marks.RemoveAll(mark => mark.Id == markId);
                    session.Interests.RemoveAll(interest => interest.MarkId == markId);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetEndpointAsync(
        Guid sessionId,
        KustoChainEndpointRole role,
        KustoRecordedValueCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(coordinate);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        return ExecuteWriteAsync(
            document =>
            {
                KustoRecordedSessionDocument session = GetSession(document, sessionId);
                _ = GetCoordinateValue(session, coordinate);
                session.Endpoints.RemoveAll(endpoint => endpoint.Role == role);
                session.Endpoints.Add(new KustoChainEndpointDocument
                {
                    SessionId = sessionId,
                    Role = role,
                    Coordinate = CreateCoordinateDocument(coordinate),
                });
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearEndpointAsync(
        Guid sessionId,
        KustoChainEndpointRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        return ExecuteWriteAsync(
            document =>
            {
                KustoRecordedSessionDocument? session = document.Sessions.FirstOrDefault(
                    candidate => candidate.Id == sessionId);
                session?.Endpoints.RemoveAll(endpoint => endpoint.Role == role);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KustoRecordedSessionSummary>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync<IReadOnlyList<KustoRecordedSessionSummary>>(
            document => document.Sessions
                .Select(CreateSummary)
                .OrderByDescending(summary => summary.LastUpdatedAtUtc)
                .ThenBy(summary => summary.Name, StringComparer.Ordinal)
                .ToArray(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KustoRecordedInterest>> GetInterestsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        return ExecuteReadAsync<IReadOnlyList<KustoRecordedInterest>>(
            document => document.Sessions.FirstOrDefault(candidate => candidate.Id == sessionId)?
                .Interests.Select(CreateInterest).ToArray()
                ?? [],
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<KustoRecordedSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        return ExecuteReadAsync(
            document =>
            {
                KustoRecordedSessionDocument? session = document.Sessions.FirstOrDefault(
                    candidate => candidate.Id == sessionId);
                return session is null ? null : CreateSession(session);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<KustoRecordedSessionSummary> ImportSessionCopyAsync(
        KustoRecordedSession source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureImportedSessionFitsLimits(
            source,
            maximumExecutionsPerSession,
            maximumResultBytesPerExecution);
        return ExecuteWriteAsync(
            document =>
            {
                if (document.Sessions.Count >= maximumSessionCount)
                {
                    throw new InvalidOperationException(
                        $"Recorded session storage contains {document.Sessions.Count:N0} sessions. "
                        + "Delete a session before importing another.");
                }

                string importedName = CreateImportedSessionName(
                    document,
                    source.Summary.Name,
                    maximumSessionCount);
                KustoRecordedSessionDocument imported = CreateImportedSessionDocument(source, importedName);
                document.Sessions.Add(imported);
                return CreateSummary(imported);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RenameExecutionAsync(
        Guid executionId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string normalizedDisplayName = displayName.Trim();
        if (normalizedDisplayName.Length > 80)
        {
            throw new ArgumentException(
                "The recorded query name cannot exceed 80 characters.",
                nameof(displayName));
        }

        return ExecuteWriteAsync(
            document =>
            {
                (KustoRecordedSessionDocument Session, KustoRecordedExecutionDocument Execution) found =
                    FindExecution(document, executionId);
                found.Execution.DisplayName = normalizedDisplayName;
                found.Session.LastUpdatedAtUtc = timeProvider.GetUtcNow();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteExecutionAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        return ExecuteWriteAsync(
            document =>
            {
                KustoRecordedSessionDocument? session = document.Sessions.FirstOrDefault(
                    candidate => candidate.Executions.Any(execution => execution.Id == executionId));
                KustoRecordedExecutionDocument? execution = session?.Executions.FirstOrDefault(
                    candidate => candidate.Id == executionId);
                if (session is null || execution is null)
                {
                    return;
                }

                if (execution.Status == KustoRecordedExecutionStatus.Running)
                {
                    throw new InvalidOperationException("A running recorded query cannot be deleted.");
                }

                session.Executions.Remove(execution);
                session.Interests.RemoveAll(interest =>
                    interest.DeclaredExecutionId == executionId
                    || interest.Coordinate?.ExecutionId == executionId);
                session.Marks.RemoveAll(mark => mark.Coordinate.ExecutionId == executionId);
                session.Endpoints.RemoveAll(endpoint => endpoint.Coordinate.ExecutionId == executionId);
                session.LastUpdatedAtUtc = timeProvider.GetUtcNow();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        return ExecuteWriteAsync(
            document => document.Sessions.RemoveAll(session => session.Id == sessionId),
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            writeGate.Dispose();
        }
    }

    private static KustoRecordedSessionSnapshotDocument DeserializeOrCreate(
        string? json,
        out bool invalidSnapshot)
    {
        invalidSnapshot = false;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new KustoRecordedSessionSnapshotDocument();
        }

        try
        {
            KustoRecordedSessionSnapshotDocument document = KustoRecordedSessionSnapshotJson.Deserialize(json);
            Validate(document);
            return document;
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or ArgumentException or FormatException or OverflowException)
        {
            invalidSnapshot = true;
            return new KustoRecordedSessionSnapshotDocument();
        }
    }

    private static void Validate(KustoRecordedSessionSnapshotDocument document)
    {
        if (document.Sessions is null)
        {
            throw new InvalidDataException("The recorded-session collection is missing.");
        }

        HashSet<Guid> sessionIds = [];
        foreach (KustoRecordedSessionDocument session in document.Sessions)
        {
            if (!sessionIds.Add(session.Id)
                || session.Periods is null
                || session.Executions is null
                || session.Interests is null
                || session.Marks is null
                || session.Endpoints is null)
            {
                throw new InvalidDataException("The recorded-session snapshot is structurally invalid.");
            }

            _ = CreateSession(session);
        }
    }

    private static void EnsureNoOpenPeriod(KustoRecordedSessionSnapshotDocument document)
    {
        if (document.Sessions.SelectMany(session => session.Periods).Any(period => period.StoppedAtUtc is null))
        {
            throw new InvalidOperationException("Another recording period is already active.");
        }
    }

    private static KustoRecordedSessionDocument GetSession(
        KustoRecordedSessionSnapshotDocument document,
        Guid sessionId)
    {
        return document.Sessions.FirstOrDefault(session => session.Id == sessionId)
            ?? throw new InvalidOperationException("The recorded session does not exist.");
    }

    private static (KustoRecordedSessionDocument Session, KustoRecordedExecutionDocument Execution) FindExecution(
        KustoRecordedSessionSnapshotDocument document,
        Guid executionId)
    {
        foreach (KustoRecordedSessionDocument session in document.Sessions)
        {
            KustoRecordedExecutionDocument? execution = session.Executions.FirstOrDefault(
                candidate => candidate.Id == executionId);
            if (execution is not null)
            {
                return (session, execution);
            }
        }

        throw new InvalidOperationException("The recorded query does not exist.");
    }

    private static KustoRecordedExecutionDocument CreateExecutionDocument(
        Guid executionId,
        Guid sessionId,
        long sequence,
        KustoRecordedExecutionStart execution)
    {
        return new KustoRecordedExecutionDocument
        {
            Id = executionId,
            SessionId = sessionId,
            PeriodId = execution.PeriodId,
            Sequence = sequence,
            DocumentId = execution.DocumentId,
            DocumentTitle = execution.DocumentTitle,
            ClusterUri = execution.Request.ClusterUri.AbsoluteUri,
            DatabaseName = execution.Request.DatabaseName,
            QueryText = execution.Request.QueryText,
            StartedAtUtc = execution.StartedAtUtc,
            Status = KustoRecordedExecutionStatus.Running,
            Relation = execution.Relation is null ? null : CreateRelationDocument(execution.Relation),
        };
    }

    private static KustoRecordedSessionDocument CreateImportedSessionDocument(
        KustoRecordedSession source,
        string importedName)
    {
        Guid sessionId = Guid.NewGuid();
        Dictionary<Guid, Guid> periodIds = source.Periods.ToDictionary(period => period.Id, _ => Guid.NewGuid());
        Dictionary<Guid, Guid> executionIds = source.Executions.ToDictionary(
            execution => execution.Id,
            _ => Guid.NewGuid());
        Dictionary<Guid, Guid> markIds = source.Marks.ToDictionary(mark => mark.Id, _ => Guid.NewGuid());
        return new KustoRecordedSessionDocument
        {
            Id = sessionId,
            Name = importedName,
            CreatedAtUtc = source.Summary.CreatedAtUtc,
            LastUpdatedAtUtc = source.Summary.LastUpdatedAtUtc,
            Periods = source.Periods.Select(period => new KustoRecordingPeriodDocument
            {
                Id = periodIds[period.Id],
                SessionId = sessionId,
                StartedAtUtc = period.StartedAtUtc,
                StoppedAtUtc = period.StoppedAtUtc,
            }).ToList(),
            Executions = source.Executions.Select(execution => new KustoRecordedExecutionDocument
            {
                Id = executionIds[execution.Id],
                SessionId = sessionId,
                PeriodId = periodIds[execution.PeriodId],
                Sequence = execution.Sequence,
                DocumentId = execution.DocumentId,
                DocumentTitle = execution.DocumentTitle,
                DisplayName = execution.DisplayName,
                ClusterUri = execution.ClusterUri.AbsoluteUri,
                DatabaseName = execution.DatabaseName,
                QueryText = execution.QueryText,
                StartedAtUtc = execution.StartedAtUtc,
                CompletedAtUtc = execution.CompletedAtUtc,
                Status = execution.Status,
                ErrorMessage = execution.ErrorMessage,
                Result = execution.Result is null ? null : CreateResultDocument(execution.Result),
                Relation = execution.Relation is null ? null : CreateRelationDocument(execution.Relation),
            }).ToList(),
            Marks = source.Marks.Select(mark => new KustoRecordedMarkDocument
            {
                Id = markIds[mark.Id],
                SessionId = sessionId,
                Kind = mark.Kind,
                Coordinate = CreateImportedCoordinateDocument(mark.Coordinate, executionIds),
                CreatedAtUtc = mark.CreatedAtUtc,
            }).ToList(),
            Interests = source.Interests.Select(interest => new KustoRecordedInterestDocument
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                DeclaredExecutionId = executionIds[interest.DeclaredExecutionId],
                Source = interest.Source,
                ColumnName = interest.ColumnName,
                TypeName = interest.Identity.TypeName,
                CanonicalValue = interest.Identity.CanonicalValue,
                IsNull = interest.Identity.IsNull,
                Coordinate = interest.Coordinate is null
                    ? null
                    : CreateImportedCoordinateDocument(interest.Coordinate, executionIds),
                LiteralStart = interest.LiteralStart,
                LiteralLength = interest.LiteralLength,
                IsSuppressed = interest.IsSuppressed,
                MarkId = interest.MarkId is Guid markId ? markIds[markId] : null,
            }).ToList(),
            Endpoints = source.Endpoints.Select(endpoint => new KustoChainEndpointDocument
            {
                SessionId = sessionId,
                Role = endpoint.Role,
                Coordinate = CreateImportedCoordinateDocument(endpoint.Coordinate, executionIds),
            }).ToList(),
        };
    }

    private static KustoRecordedValueCoordinateDocument CreateImportedCoordinateDocument(
        KustoRecordedValueCoordinate coordinate,
        Dictionary<Guid, Guid> executionIds)
    {
        return new KustoRecordedValueCoordinateDocument
        {
            ExecutionId = executionIds[coordinate.ExecutionId],
            TableOrdinal = coordinate.TableOrdinal,
            RowOrdinal = coordinate.RowOrdinal,
            ColumnOrdinal = coordinate.ColumnOrdinal,
        };
    }

    private static KustoRecordedRelationDocument CreateRelationDocument(
        KustoRecordedRelationDescriptor relation)
    {
        return new KustoRecordedRelationDocument
        {
            SourceTableName = relation.SourceTableName,
            IsComposable = relation.IsComposable,
            Columns = relation.Columns.Select(column => new KustoSourceColumnLineageDocument
            {
                ResultColumnName = column.ResultColumnName,
                SourceColumnName = column.SourceColumnName,
            }).ToList(),
        };
    }

    private static KustoRecordedResultDocument CreateResultDocument(KustoQueryResult result)
    {
        return new KustoRecordedResultDocument
        {
            DurationTicks = result.Duration.Ticks,
            Completeness = result.Completeness,
            Tables = result.Tables.Select(table => new KustoRecordedResultTableDocument
            {
                Name = table.Name,
                Columns = table.Columns.Select(column => new KustoRecordedResultColumnDocument
                {
                    Name = column.Name,
                    TypeName = column.TypeName,
                }).ToList(),
                Rows = table.Rows.Select(row => new KustoRecordedResultRowDocument
                {
                    Values = row.ResultValues.Select(value => new KustoRecordedResultValueDocument
                    {
                        DisplayText = value.DisplayText,
                        RawJson = value.RawJson,
                        IsNull = value.IsNull,
                    }).ToList(),
                }).ToList(),
            }).ToList(),
        };
    }

    private static KustoRecordedValueCoordinateDocument CreateCoordinateDocument(
        KustoRecordedValueCoordinate coordinate)
    {
        return new KustoRecordedValueCoordinateDocument
        {
            ExecutionId = coordinate.ExecutionId,
            TableOrdinal = coordinate.TableOrdinal,
            RowOrdinal = coordinate.RowOrdinal,
            ColumnOrdinal = coordinate.ColumnOrdinal,
        };
    }

    private static (Guid ExecutionId, int TableOrdinal, int RowOrdinal, int ColumnOrdinal) ToCoordinateKey(
        KustoRecordedValueCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return (
            coordinate.ExecutionId,
            coordinate.TableOrdinal,
            coordinate.RowOrdinal,
            coordinate.ColumnOrdinal);
    }

    private static (
        KustoRecordedResultColumnDocument Column,
        KustoRecordedResultValueDocument Value) GetCoordinateValue(
            KustoRecordedSessionDocument session,
            KustoRecordedValueCoordinate coordinate)
    {
        KustoRecordedExecutionDocument? execution = session.Executions.FirstOrDefault(
            candidate => candidate.Id == coordinate.ExecutionId);
        if (execution?.Result is null
            || coordinate.TableOrdinal >= execution.Result.Tables.Count)
        {
            throw new InvalidOperationException(
                "The recorded result coordinate does not exist in the session.");
        }

        KustoRecordedResultTableDocument table = execution.Result.Tables[coordinate.TableOrdinal];
        if (coordinate.RowOrdinal >= table.Rows.Count
            || coordinate.ColumnOrdinal >= table.Columns.Count
            || coordinate.ColumnOrdinal >= table.Rows[coordinate.RowOrdinal].Values.Count)
        {
            throw new InvalidOperationException(
                "The recorded result coordinate does not exist in the session.");
        }

        return (
            table.Columns[coordinate.ColumnOrdinal],
            table.Rows[coordinate.RowOrdinal].Values[coordinate.ColumnOrdinal]);
    }

    private static KustoRecordedMark AddMark(
        KustoRecordedSessionDocument session,
        KustoRecordedMarkKind kind,
        KustoRecordedValueCoordinate coordinate,
        DateTimeOffset createdAtUtc,
        KustoRecordedInterestSource source)
    {
        KustoRecordedMarkDocument? existing = session.Marks.FirstOrDefault(mark =>
            mark.Kind == kind && CoordinatesEqual(mark.Coordinate, coordinate));
        if (existing is not null)
        {
            return CreateMark(existing);
        }

        (KustoRecordedResultColumnDocument Column, KustoRecordedResultValueDocument Value) occurrence =
            GetCoordinateValue(session, coordinate);
        Guid markId = Guid.NewGuid();
        KustoRecordedMarkDocument mark = new()
        {
            Id = markId,
            SessionId = session.Id,
            Kind = kind,
            Coordinate = CreateCoordinateDocument(coordinate),
            CreatedAtUtc = createdAtUtc.ToUniversalTime(),
        };
        session.Marks.Add(mark);
        KustoRecordedValueIdentity identity = KustoRecordedValueCanonicalizer.Create(
            occurrence.Column.TypeName,
            new KustoResultValue(
                occurrence.Value.DisplayText,
                occurrence.Value.RawJson,
                occurrence.Value.IsNull));
        session.Interests.Add(new KustoRecordedInterestDocument
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            DeclaredExecutionId = coordinate.ExecutionId,
            Source = source,
            ColumnName = occurrence.Column.Name,
            TypeName = identity.TypeName,
            CanonicalValue = identity.CanonicalValue,
            IsNull = identity.IsNull,
            Coordinate = CreateCoordinateDocument(coordinate),
            MarkId = markId,
        });
        return CreateMark(mark);
    }

    private static bool CoordinatesEqual(
        KustoRecordedValueCoordinateDocument left,
        KustoRecordedValueCoordinate right)
    {
        return left.ExecutionId == right.ExecutionId
            && left.TableOrdinal == right.TableOrdinal
            && left.RowOrdinal == right.RowOrdinal
            && left.ColumnOrdinal == right.ColumnOrdinal;
    }

    private static string CreateImportedSessionName(
        KustoRecordedSessionSnapshotDocument document,
        string sourceName,
        int maximumSessionCount)
    {
        string trimmedName = sourceName.Trim();
        if (!SessionNameExists(document, trimmedName))
        {
            return trimmedName;
        }

        string importedName = $"{trimmedName} (imported)";
        if (!SessionNameExists(document, importedName))
        {
            return importedName;
        }

        for (int suffix = 2; suffix <= maximumSessionCount + 1; suffix++)
        {
            importedName = $"{trimmedName} (imported {suffix})";
            if (!SessionNameExists(document, importedName))
            {
                return importedName;
            }
        }

        throw new InvalidOperationException("A unique imported session name could not be created.");
    }

    private static bool SessionNameExists(
        KustoRecordedSessionSnapshotDocument document,
        string name)
    {
        return document.Sessions.Any(session => string.Equals(
            session.Name,
            name,
            StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureImportedSessionFitsLimits(
        KustoRecordedSession session,
        int maximumExecutionsPerSession,
        int maximumResultBytesPerExecution)
    {
        if (session.Executions.Count > maximumExecutionsPerSession)
        {
            throw new InvalidOperationException(
                $"The archived session contains {session.Executions.Count:N0} recorded queries; "
                + $"the configured limit is {maximumExecutionsPerSession:N0}.");
        }

        foreach (KustoRecordedExecution execution in session.Executions)
        {
            if (Encoding.UTF8.GetByteCount(execution.QueryText) > MaximumRecordedQueryBytes)
            {
                throw new InvalidOperationException(
                    $"Recorded query {execution.Sequence:N0} exceeds the "
                    + $"{MaximumRecordedQueryBytes / 1024:N0} KiB query limit.");
            }

            if (execution.Result is not null
                && !ReferenceEquals(
                    execution.Result,
                    KustoRecordedResultLimiter.Limit(
                        execution.Result,
                        MaximumRecordedRowsPerExecution,
                        maximumResultBytesPerExecution)))
            {
                throw new InvalidOperationException(
                    $"Recorded query {execution.Sequence:N0} exceeds the configured result-size limit.");
            }
        }
    }

    private static void AddConfirmedPredicateMarks(
        KustoRecordedSessionDocument session,
        KustoRecordedExecutionDocument execution,
        DateTimeOffset createdAtUtc)
    {
        KustoRecordedInterestDocument[] predicateInterests = session.Interests.Where(interest =>
            interest.DeclaredExecutionId == execution.Id
            && interest.Source == KustoRecordedInterestSource.QueryPredicate
            && !interest.IsSuppressed).ToArray();
        foreach (KustoRecordedInterestDocument interest in predicateInterests)
        {
            KustoRecordedValueCoordinate? coordinate = FindMatchingCoordinate(execution, interest);
            if (coordinate is not null)
            {
                _ = AddMark(
                    session,
                    KustoRecordedMarkKind.Cell,
                    coordinate,
                    createdAtUtc,
                    KustoRecordedInterestSource.ConfirmedPredicate);
            }
        }
    }

    private static KustoRecordedValueCoordinate? FindMatchingCoordinate(
        KustoRecordedExecutionDocument execution,
        KustoRecordedInterestDocument interest)
    {
        if (execution.Result is null)
        {
            return null;
        }

        for (int tableIndex = 0; tableIndex < execution.Result.Tables.Count; tableIndex++)
        {
            KustoRecordedResultTableDocument table = execution.Result.Tables[tableIndex];
            KustoRecordedValueCoordinate? coordinate = FindMatchingCoordinate(
                execution,
                table,
                tableIndex,
                interest);
            if (coordinate is not null)
            {
                return coordinate;
            }
        }

        return null;
    }

    private static KustoRecordedValueCoordinate? FindMatchingCoordinate(
        KustoRecordedExecutionDocument execution,
        KustoRecordedResultTableDocument table,
        int tableIndex,
        KustoRecordedInterestDocument interest)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                if (ColumnMatches(table.Columns[columnIndex].Name, execution.Relation, interest.ColumnName)
                    && ValueMatches(table.Columns[columnIndex], table.Rows[rowIndex].Values[columnIndex], interest))
                {
                    return new KustoRecordedValueCoordinate(
                        execution.Id,
                        tableIndex,
                        rowIndex,
                        columnIndex);
                }
            }
        }

        return null;
    }

    private static bool ColumnMatches(
        string resultColumnName,
        KustoRecordedRelationDocument? relation,
        string interestColumnName)
    {
        return string.Equals(resultColumnName, interestColumnName, StringComparison.OrdinalIgnoreCase)
            || relation?.Columns.Any(column =>
                string.Equals(column.ResultColumnName, resultColumnName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(column.SourceColumnName, interestColumnName, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool ValueMatches(
        KustoRecordedResultColumnDocument column,
        KustoRecordedResultValueDocument value,
        KustoRecordedInterestDocument interest)
    {
        KustoRecordedValueIdentity identity = KustoRecordedValueCanonicalizer.Create(
            column.TypeName,
            new KustoResultValue(value.DisplayText, value.RawJson, value.IsNull));
        return identity.IsNull == interest.IsNull
            && string.Equals(identity.TypeName, interest.TypeName, StringComparison.Ordinal)
            && string.Equals(identity.CanonicalValue, interest.CanonicalValue, StringComparison.Ordinal);
    }

    private static KustoRecordedSession CreateSession(KustoRecordedSessionDocument session)
    {
        return new KustoRecordedSession(
            CreateSummary(session),
            session.Periods.Select(CreatePeriod),
            session.Executions.OrderBy(execution => execution.Sequence).Select(CreateExecution),
            session.Interests.Select(CreateInterest),
            session.Marks.OrderBy(mark => mark.CreatedAtUtc).Select(CreateMark),
            session.Endpoints.OrderBy(endpoint => endpoint.Role).Select(CreateEndpoint));
    }

    private static KustoRecordedSessionSummary CreateSummary(KustoRecordedSessionDocument session)
    {
        KustoRecordedSessionSnapshotDocument singleSession = new()
        {
            Sessions = [session],
        };
        long storedBytes = Encoding.UTF8.GetByteCount(
            KustoRecordedSessionSnapshotJson.Serialize(singleSession));
        return new KustoRecordedSessionSummary(
            session.Id,
            session.Name,
            session.CreatedAtUtc,
            session.LastUpdatedAtUtc,
            session.Executions.Count,
            storedBytes);
    }

    private static KustoRecordingPeriod CreatePeriod(KustoRecordingPeriodDocument period)
    {
        return new KustoRecordingPeriod(
            period.Id,
            period.SessionId,
            period.StartedAtUtc,
            period.StoppedAtUtc);
    }

    private static KustoRecordedExecution CreateExecution(KustoRecordedExecutionDocument execution)
    {
        return new KustoRecordedExecution(
            execution.Id,
            execution.SessionId,
            execution.PeriodId,
            execution.Sequence,
            execution.DocumentId,
            execution.DocumentTitle,
            new Uri(execution.ClusterUri, UriKind.Absolute),
            execution.DatabaseName,
            execution.QueryText,
            execution.StartedAtUtc,
            execution.CompletedAtUtc,
            execution.Status,
            execution.ErrorMessage,
            execution.Result is null ? null : CreateResult(execution.Result),
            execution.Relation is null ? null : CreateRelation(execution.Relation),
            execution.DisplayName);
    }

    private static KustoQueryResult CreateResult(KustoRecordedResultDocument result)
    {
        return new KustoQueryResult(
            result.Tables.Select(table => new KustoResultTable(
                table.Name,
                table.Columns.Select(column => new KustoResultColumn(column.Name, column.TypeName)),
                table.Rows.Select(row => new KustoResultRow(row.Values.Select(value =>
                    new KustoResultValue(value.DisplayText, value.RawJson, value.IsNull)))))),
            TimeSpan.FromTicks(result.DurationTicks),
            completeness: result.Completeness);
    }

    private static KustoRecordedRelationDescriptor CreateRelation(KustoRecordedRelationDocument relation)
    {
        return new KustoRecordedRelationDescriptor(
            relation.SourceTableName,
            relation.IsComposable,
            relation.Columns.Select(column => new KustoSourceColumnLineage(
                column.ResultColumnName,
                column.SourceColumnName)));
    }

    private static KustoRecordedInterest CreateInterest(KustoRecordedInterestDocument interest)
    {
        return new KustoRecordedInterest(
            interest.Id,
            interest.SessionId,
            interest.DeclaredExecutionId,
            interest.Source,
            interest.ColumnName,
            new KustoRecordedValueIdentity(
                interest.TypeName,
                interest.CanonicalValue,
                interest.IsNull),
            interest.Coordinate is null ? null : CreateCoordinate(interest.Coordinate),
            interest.LiteralStart,
            interest.LiteralLength,
            interest.IsSuppressed,
            interest.MarkId);
    }

    private static KustoRecordedMark CreateMark(KustoRecordedMarkDocument mark)
    {
        return new KustoRecordedMark(
            mark.Id,
            mark.SessionId,
            mark.Kind,
            CreateCoordinate(mark.Coordinate),
            mark.CreatedAtUtc);
    }

    private static KustoChainEndpoint CreateEndpoint(KustoChainEndpointDocument endpoint)
    {
        return new KustoChainEndpoint(
            endpoint.SessionId,
            endpoint.Role,
            CreateCoordinate(endpoint.Coordinate));
    }

    private static KustoRecordedValueCoordinate CreateCoordinate(
        KustoRecordedValueCoordinateDocument coordinate)
    {
        return new KustoRecordedValueCoordinate(
            coordinate.ExecutionId,
            coordinate.TableOrdinal,
            coordinate.RowOrdinal,
            coordinate.ColumnOrdinal);
    }

    private static void RecoverInterruptedPeriods(
        KustoRecordedSessionDocument session,
        DateTimeOffset recoveredAtUtc)
    {
        foreach (KustoRecordingPeriodDocument period in session.Periods.Where(
            period => period.StoppedAtUtc is null))
        {
            period.StoppedAtUtc = recoveredAtUtc < period.StartedAtUtc
                ? period.StartedAtUtc
                : recoveredAtUtc;
        }
    }

    private static void RecoverInterruptedExecutions(
        KustoRecordedSessionDocument session,
        DateTimeOffset recoveredAtUtc)
    {
        foreach (KustoRecordedExecutionDocument execution in session.Executions.Where(
            execution => execution.Status == KustoRecordedExecutionStatus.Running))
        {
            execution.Status = KustoRecordedExecutionStatus.Interrupted;
            execution.CompletedAtUtc = recoveredAtUtc < execution.StartedAtUtc
                ? execution.StartedAtUtc
                : recoveredAtUtc;
            execution.ErrorMessage = "Recording interrupted when the application stopped.";
        }
    }

    private async Task RecoverInterruptedRecordingsAsync(CancellationToken cancellationToken)
    {
        if (!snapshot.Sessions.Any(session =>
            session.Periods.Any(period => period.StoppedAtUtc is null)
            || session.Executions.Any(execution => execution.Status == KustoRecordedExecutionStatus.Running)))
        {
            return;
        }

        DateTimeOffset recoveredAtUtc = timeProvider.GetUtcNow();
        await ExecuteWriteAsync(
            document =>
            {
                foreach (KustoRecordedSessionDocument session in document.Sessions)
                {
                    RecoverInterruptedPeriods(session, recoveredAtUtc);
                    RecoverInterruptedExecutions(session, recoveredAtUtc);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private Task<bool> ExecuteWriteAsync(
        Action<KustoRecordedSessionSnapshotDocument> action,
        CancellationToken cancellationToken)
    {
        return ExecuteWriteAsync(
            document =>
            {
                action(document);
                return true;
            },
            cancellationToken);
    }

    private async Task<TResult> ExecuteWriteAsync<TResult>(
        Func<KustoRecordedSessionSnapshotDocument, TResult> action,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string originalJson = KustoRecordedSessionSnapshotJson.Serialize(snapshot);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            TResult result = action(snapshot);
            string updatedJson = KustoRecordedSessionSnapshotJson.Serialize(snapshot);
            if (Encoding.UTF8.GetByteCount(updatedJson) > maximumSnapshotBytes)
            {
                throw new InvalidOperationException(
                    "Recorded session storage is full. Delete recorded queries or sessions before recording more data.");
            }

            await snapshotStore.SaveAsync(updatedJson, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            snapshot = KustoRecordedSessionSnapshotJson.Deserialize(originalJson);
            throw;
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task<TResult> ExecuteReadAsync<TResult>(
        Func<KustoRecordedSessionSnapshotDocument, TResult> action,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action(snapshot);
        }
        finally
        {
            writeGate.Release();
        }
    }
}
