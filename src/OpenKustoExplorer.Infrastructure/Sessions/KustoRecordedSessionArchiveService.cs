using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Infrastructure.Sessions;

/// <summary>
/// Exports and imports portable recorded-session archives.
/// </summary>
public sealed class KustoRecordedSessionArchiveService : IKustoRecordedSessionArchiveService
{
    /// <summary>Gets the required manifest entry name.</summary>
    internal const string ManifestEntryName = "manifest.json";

    /// <summary>Gets the required session payload entry name.</summary>
    internal const string SessionEntryName = "session.json";

    private const long MaximumArchiveBytes = SqliteKustoRecordedSessionStore.MaximumDatabaseBytes;
    private const long MaximumManifestBytes = 64 * 1024;
    private const long MaximumSessionEntryBytes = SqliteKustoRecordedSessionStore.MaximumDatabaseBytes;
    private readonly SqliteKustoRecordedSessionStore store;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedSessionArchiveService"/> class.
    /// </summary>
    /// <param name="store">The recorded-session store.</param>
    /// <param name="timeProvider">The application clock.</param>
    public KustoRecordedSessionArchiveService(
        SqliteKustoRecordedSessionStore store,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.store = store;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task ExportAsync(
        Guid sessionId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The archive destination must be writable.", nameof(destination));
        }

        KustoRecordedSession session = await store.GetSessionAsync(sessionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The recorded session no longer exists.");
        EnsureFinalizedForExport(session);
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        using ZipArchive archive = new(destination, ZipArchiveMode.Create, leaveOpen: true);
        ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        await using (Stream manifestStream = await manifestEntry.OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            KustoRecordedSessionArchiveJson.WriteManifest(
                manifestStream,
                session,
                timeProvider.GetUtcNow());
        }

        cancellationToken.ThrowIfCancellationRequested();
        ZipArchiveEntry sessionEntry = archive.CreateEntry(SessionEntryName, CompressionLevel.Optimal);
        await using Stream sessionStream = await sessionEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
        KustoRecordedSessionArchiveJson.WriteSession(sessionStream, session);
    }

    /// <inheritdoc />
    public async Task<KustoRecordedSessionSummary> ImportCopyAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The session archive source must be readable.", nameof(source));
        }

        using MemoryStream? bufferedSource = await BufferIfRequiredAsync(source, cancellationToken)
            .ConfigureAwait(false);
        Stream archiveStream = bufferedSource ?? source;
        EnsureArchiveLength(archiveStream);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            (ZipArchiveEntry Manifest, ZipArchiveEntry Session) entries = GetRequiredEntries(archive);
            EnsureEntryLength(entries.Manifest, MaximumManifestBytes);
            EnsureEntryLength(entries.Session, MaximumSessionEntryBytes);

            (Guid SessionId, string SessionName) manifest;
            await using (Stream manifestStream = await entries.Manifest.OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                manifest = KustoRecordedSessionArchiveJson.ReadManifest(manifestStream);
            }

            cancellationToken.ThrowIfCancellationRequested();
            KustoRecordedSession session;
            await using (Stream sessionStream = await entries.Session.OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                session = KustoRecordedSessionArchiveJson.ReadSession(sessionStream);
            }

            if (manifest.SessionId != session.Summary.Id
                || !string.Equals(manifest.SessionName, session.Summary.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The archive manifest does not match its session payload.");
            }

            ValidateSession(session);
            return await store.ImportSessionCopyAsync(session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException
            or FormatException
            or OverflowException
            or ArgumentException)
        {
            throw new InvalidDataException("The recorded-session archive contains invalid data.", exception);
        }
    }

    private static async Task<MemoryStream?> BufferIfRequiredAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        if (source.CanSeek)
        {
            return null;
        }

        MemoryStream buffer = new();
        byte[] bytes = new byte[81920];
        while (true)
        {
            int read = await source.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaximumArchiveBytes)
            {
                await buffer.DisposeAsync().ConfigureAwait(false);
                throw new InvalidDataException("The recorded-session archive is too large.");
            }

            await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static void EnsureArchiveLength(Stream stream)
    {
        if (stream.CanSeek && stream.Length - stream.Position > MaximumArchiveBytes)
        {
            throw new InvalidDataException("The recorded-session archive is too large.");
        }
    }

    private static (ZipArchiveEntry Manifest, ZipArchiveEntry Session) GetRequiredEntries(ZipArchive archive)
    {
        if (archive.Entries.Count != 2)
        {
            throw new InvalidDataException(
                $"A recorded-session archive must contain only {ManifestEntryName} and {SessionEntryName}.");
        }

        ZipArchiveEntry[] manifests = archive.Entries
            .Where(entry => string.Equals(entry.FullName, ManifestEntryName, StringComparison.Ordinal))
            .ToArray();
        ZipArchiveEntry[] sessions = archive.Entries
            .Where(entry => string.Equals(entry.FullName, SessionEntryName, StringComparison.Ordinal))
            .ToArray();
        if (manifests.Length != 1 || sessions.Length != 1)
        {
            throw new InvalidDataException(
                $"A recorded-session archive requires one {ManifestEntryName} and one {SessionEntryName} entry.");
        }

        return (manifests[0], sessions[0]);
    }

    private static void EnsureEntryLength(ZipArchiveEntry entry, long maximumLength)
    {
        if (entry.Length > maximumLength)
        {
            throw new InvalidDataException($"Archive entry '{entry.FullName}' is too large.");
        }
    }

    private static void EnsureFinalizedForExport(KustoRecordedSession session)
    {
        if (session.Periods.Any(period => period.StoppedAtUtc is null)
            || session.Executions.Any(execution => execution.Status == KustoRecordedExecutionStatus.Running))
        {
            throw new InvalidOperationException("Stop the recording session before exporting it.");
        }
    }

    private static void ValidateSession(KustoRecordedSession session)
    {
        if (session.Summary.CreatedAtUtc > session.Summary.LastUpdatedAtUtc)
        {
            throw new InvalidDataException("The session update time precedes its creation time.");
        }

        if (session.Periods.Count == 0)
        {
            throw new InvalidDataException("A recorded session must contain at least one recording period.");
        }

        Dictionary<Guid, KustoRecordingPeriod> periods = CreateUniqueMap(
            session.Periods,
            period => period.Id,
            "recording period");
        ValidatePeriods(session, periods.Values);
        Dictionary<Guid, KustoRecordedExecution> executions = CreateUniqueMap(
            session.Executions,
            execution => execution.Id,
            "recorded execution");
        ValidateExecutions(session, executions.Values, periods);
        Dictionary<Guid, KustoRecordedMark> marks = CreateUniqueMap(
            session.Marks,
            mark => mark.Id,
            "recorded mark");
        ValidateMarks(session, marks.Values, executions);
        ValidateInterests(session, executions, marks);
        ValidateEndpoints(session, executions);
    }

    private static Dictionary<Guid, T> CreateUniqueMap<T>(
        IEnumerable<T> values,
        Func<T, Guid> keySelector,
        string description)
    {
        Dictionary<Guid, T> result = [];
        if (values.Any(value => !result.TryAdd(keySelector(value), value)))
        {
            throw new InvalidDataException($"The archive contains a duplicate {description} identifier.");
        }

        return result;
    }

    private static void ValidatePeriods(
        KustoRecordedSession session,
        IEnumerable<KustoRecordingPeriod> periods)
    {
        foreach (KustoRecordingPeriod period in periods)
        {
            if (period.SessionId != session.Summary.Id)
            {
                throw new InvalidDataException("A recording period belongs to a different session.");
            }

            if (period.StoppedAtUtc is null)
            {
                throw new InvalidDataException("An archived recording period is still open.");
            }
        }
    }

    private static void ValidateExecutions(
        KustoRecordedSession session,
        IEnumerable<KustoRecordedExecution> executions,
        Dictionary<Guid, KustoRecordingPeriod> periods)
    {
        HashSet<long> sequences = [];
        foreach (KustoRecordedExecution execution in executions)
        {
            if (execution.SessionId != session.Summary.Id)
            {
                throw new InvalidDataException("A recorded execution belongs to a different session.");
            }

            if (!periods.ContainsKey(execution.PeriodId))
            {
                throw new InvalidDataException("A recorded execution references an unknown recording period.");
            }

            if (!sequences.Add(execution.Sequence))
            {
                throw new InvalidDataException("The archive contains a duplicate execution sequence.");
            }

            if (execution.Status == KustoRecordedExecutionStatus.Running
                || execution.CompletedAtUtc is null)
            {
                throw new InvalidDataException("An archived recorded execution is not finalized.");
            }

            if (execution.CompletedAtUtc < execution.StartedAtUtc)
            {
                throw new InvalidDataException("A recorded execution completes before it starts.");
            }

            if (execution.Status == KustoRecordedExecutionStatus.Succeeded && execution.Result is null)
            {
                throw new InvalidDataException("A successful recorded execution is missing its result.");
            }

            ValidateResult(execution);
        }
    }

    private static void ValidateResult(KustoRecordedExecution execution)
    {
        if (execution.Result is null)
        {
            return;
        }

        int rowCount = 0;
        foreach (KustoResultTable table in execution.Result.Tables)
        {
            rowCount += table.Rows.Count;
            foreach (KustoResultRow row in table.Rows)
            {
                if (row.ResultValues.Count != table.Columns.Count)
                {
                    throw new InvalidDataException("A recorded result row does not match its column count.");
                }
            }
        }

        if (rowCount > SqliteKustoRecordedSessionStore.MaximumRecordedRowsPerExecution)
        {
            throw new InvalidDataException("A recorded execution exceeds the result row limit.");
        }

        if (Encoding.UTF8.GetByteCount(execution.QueryText)
            > SqliteKustoRecordedSessionStore.MaximumRecordedQueryBytes)
        {
            throw new InvalidDataException("A recorded execution exceeds the query text limit.");
        }
    }

    private static void ValidateMarks(
        KustoRecordedSession session,
        IEnumerable<KustoRecordedMark> marks,
        Dictionary<Guid, KustoRecordedExecution> executions)
    {
        HashSet<(KustoRecordedMarkKind, Guid, int, int, int)> coordinates = [];
        foreach (KustoRecordedMark mark in marks)
        {
            if (mark.SessionId != session.Summary.Id)
            {
                throw new InvalidDataException("A recorded mark belongs to a different session.");
            }

            ValidateCoordinate(mark.Coordinate, executions);
            if (!coordinates.Add((
                mark.Kind,
                mark.Coordinate.ExecutionId,
                mark.Coordinate.TableOrdinal,
                mark.Coordinate.RowOrdinal,
                mark.Coordinate.ColumnOrdinal)))
            {
                throw new InvalidDataException("The archive contains duplicate recorded marks.");
            }
        }
    }

    private static void ValidateInterests(
        KustoRecordedSession session,
        Dictionary<Guid, KustoRecordedExecution> executions,
        Dictionary<Guid, KustoRecordedMark> marks)
    {
        HashSet<Guid> interestIds = [];
        HashSet<Guid> linkedMarkIds = [];
        foreach (KustoRecordedInterest interest in session.Interests)
        {
            if (!interestIds.Add(interest.Id))
            {
                throw new InvalidDataException("The archive contains a duplicate recorded interest identifier.");
            }

            if (interest.SessionId != session.Summary.Id
                || !executions.TryGetValue(interest.DeclaredExecutionId, out KustoRecordedExecution? execution))
            {
                throw new InvalidDataException("A recorded interest references an unknown session or execution.");
            }

            ValidateInterestShape(interest, execution, executions, marks, linkedMarkIds);
        }

        if (linkedMarkIds.Count != marks.Count)
        {
            throw new InvalidDataException("Every recorded mark must have one linked value interest.");
        }
    }

    private static void ValidateInterestShape(
        KustoRecordedInterest interest,
        KustoRecordedExecution declaringExecution,
        Dictionary<Guid, KustoRecordedExecution> executions,
        Dictionary<Guid, KustoRecordedMark> marks,
        HashSet<Guid> linkedMarkIds)
    {
        if (interest.Identity.IsNull)
        {
            throw new InvalidDataException("Recorded interests cannot identify null values.");
        }

        if (interest.Source == KustoRecordedInterestSource.QueryPredicate)
        {
            if (interest.Coordinate is not null
                || interest.MarkId is not null
                || interest.LiteralStart is not int literalStart
                || interest.LiteralLength is not int literalLength
                || literalStart < 0
                || literalLength <= 0
                || literalStart > declaringExecution.QueryText.Length
                || literalLength > declaringExecution.QueryText.Length - literalStart)
            {
                throw new InvalidDataException("A predicate interest has inconsistent source coordinates.");
            }

            return;
        }

        if (interest.Coordinate is not KustoRecordedValueCoordinate coordinate
            || interest.MarkId is not Guid markId
            || interest.LiteralStart is not null
            || interest.LiteralLength is not null
            || !marks.TryGetValue(markId, out KustoRecordedMark? mark)
            || !CoordinatesEqual(coordinate, mark.Coordinate)
            || !linkedMarkIds.Add(markId))
        {
            throw new InvalidDataException("A marked interest has inconsistent mark linkage.");
        }

        (KustoResultColumn Column, KustoResultValue Value) value = ValidateCoordinate(coordinate, executions);
        KustoRecordedValueIdentity expected = KustoRecordedValueCanonicalizer.Create(
            value.Column.TypeName,
            value.Value);
        if (!interest.Identity.Equals(expected)
            || !string.Equals(interest.ColumnName, value.Column.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A marked interest does not match its recorded result value.");
        }
    }

    private static void ValidateEndpoints(
        KustoRecordedSession session,
        Dictionary<Guid, KustoRecordedExecution> executions)
    {
        HashSet<KustoChainEndpointRole> roles = [];
        foreach (KustoChainEndpoint endpoint in session.Endpoints)
        {
            if (endpoint.SessionId != session.Summary.Id || !roles.Add(endpoint.Role))
            {
                throw new InvalidDataException("The archive contains inconsistent chain endpoints.");
            }

            ValidateCoordinate(endpoint.Coordinate, executions);
        }
    }

    private static (KustoResultColumn Column, KustoResultValue Value) ValidateCoordinate(
        KustoRecordedValueCoordinate coordinate,
        Dictionary<Guid, KustoRecordedExecution> executions)
    {
        if (!executions.TryGetValue(coordinate.ExecutionId, out KustoRecordedExecution? execution)
            || execution.Result is null
            || coordinate.TableOrdinal >= execution.Result.Tables.Count)
        {
            throw new InvalidDataException("A recorded value coordinate references an unknown result table.");
        }

        KustoResultTable table = execution.Result.Tables[coordinate.TableOrdinal];
        if (coordinate.RowOrdinal >= table.Rows.Count || coordinate.ColumnOrdinal >= table.Columns.Count)
        {
            throw new InvalidDataException("A recorded value coordinate is outside its result table.");
        }

        return (table.Columns[coordinate.ColumnOrdinal], table.Rows[coordinate.RowOrdinal].ResultValues[coordinate.ColumnOrdinal]);
    }

    private static bool CoordinatesEqual(
        KustoRecordedValueCoordinate left,
        KustoRecordedValueCoordinate right)
    {
        return left.ExecutionId == right.ExecutionId
            && left.TableOrdinal == right.TableOrdinal
            && left.RowOrdinal == right.RowOrdinal
            && left.ColumnOrdinal == right.ColumnOrdinal;
    }
}
