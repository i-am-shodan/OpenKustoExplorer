using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Application.Diagnostics;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Infrastructure.Sessions;

/// <summary>
/// Persists query-recording sessions in a transactional local SQLite database.
/// </summary>
public sealed class SqliteKustoRecordedSessionStore :
    IKustoRecordedSessionStore,
    IKustoRecordedSessionStoreMaintenance,
    IDisposable
{
    /// <summary>Gets the maximum total rows persisted for one recorded execution.</summary>
    internal const int MaximumRecordedRowsPerExecution = 500;

    /// <summary>Gets the maximum estimated result payload persisted for one execution.</summary>
    internal const int MaximumRecordedResultBytesPerExecution = 2 * 1024 * 1024;

    /// <summary>Gets the maximum query text persisted for one execution.</summary>
    internal const int MaximumRecordedQueryBytes = 256 * 1024;

    /// <summary>Gets the maximum number of retained sessions.</summary>
    internal const int MaximumSessionCount = 100;

    /// <summary>Gets the maximum number of executions retained by one session.</summary>
    internal const int MaximumExecutionsPerSession = 500;

    /// <summary>Gets the maximum logical database bytes used by recorded sessions.</summary>
    internal const long MaximumDatabaseBytes = 1024L * 1024 * 1024;

    private const int CompletionMetadataReserveBytes = 64 * 1024;
    private const string SelectSessionSummariesSql = """
        SELECT
            s.id,
            s.name,
            s.created_at_utc,
            s.last_updated_at_utc,
            (SELECT COUNT(*) FROM recorded_executions e WHERE e.session_id = s.id),
            length(CAST(s.id || s.name || s.normalized_name || s.created_at_utc || s.last_updated_at_utc AS BLOB))
            + COALESCE((
                SELECT SUM(length(CAST(
                    p.id || p.session_id || p.started_at_utc || IFNULL(p.stopped_at_utc, '')
                    AS BLOB)))
                FROM recording_periods p WHERE p.session_id = s.id), 0)
            + COALESCE((
                SELECT SUM(length(CAST(
                    e.id || e.session_id || e.period_id || e.sequence || e.document_id
                    || e.document_title || IFNULL(e.display_name, '')
                    || e.cluster_uri || e.database_name || e.query_text
                    || e.started_at_utc || IFNULL(e.completed_at_utc, '') || e.status
                    || IFNULL(e.error_message, '') || IFNULL(e.duration_milliseconds, '')
                    || e.completeness || IFNULL(e.source_table_name, '') || e.is_composable
                    AS BLOB)))
                FROM recorded_executions e WHERE e.session_id = s.id), 0)
            + COALESCE((
                SELECT SUM(length(CAST(
                    c.execution_id || c.ordinal || c.result_column_name || c.source_column_name
                    AS BLOB)))
                FROM recorded_relation_columns c
                JOIN recorded_executions e ON e.id = c.execution_id
                WHERE e.session_id = s.id), 0)
            + COALESCE((
                SELECT SUM(length(CAST(t.id || t.execution_id || t.ordinal || t.name AS BLOB)))
                FROM recorded_result_tables t
                JOIN recorded_executions e ON e.id = t.execution_id
                WHERE e.session_id = s.id), 0)
            + COALESCE((
                SELECT SUM(length(CAST(c.table_id || c.ordinal || c.name || c.type_name AS BLOB)))
                FROM recorded_result_columns c
                JOIN recorded_result_tables t ON t.id = c.table_id
                JOIN recorded_executions e ON e.id = t.execution_id
                WHERE e.session_id = s.id), 0)
            + COALESCE((
                SELECT SUM(length(CAST(
                    r.id || r.table_id || r.ordinal || r.display_values_json
                    || r.raw_values_json || r.row_key
                    AS BLOB)))
                FROM recorded_result_rows r
                JOIN recorded_result_tables t ON t.id = r.table_id
                JOIN recorded_executions e ON e.id = t.execution_id
                WHERE e.session_id = s.id), 0)
            + COALESCE((
                SELECT SUM(length(CAST(
                    i.id || i.session_id || i.declared_execution_id || i.source || i.column_name
                    || i.type_name || i.canonical_value || i.value_hash
                    || IFNULL(i.coordinate_execution_id, '') || IFNULL(i.table_ordinal, '')
                    || IFNULL(i.row_ordinal, '') || IFNULL(i.column_ordinal, '')
                    || IFNULL(i.literal_start, '') || IFNULL(i.literal_length, '')
                    || IFNULL(i.mark_id, '') || i.is_suppressed
                    AS BLOB)))
                FROM recorded_value_interests i WHERE i.session_id = s.id), 0)
            + COALESCE((
                SELECT SUM(length(CAST(
                    m.id || m.session_id || m.kind || m.execution_id || m.table_ordinal
                    || m.row_ordinal || m.column_ordinal || m.created_at_utc
                    AS BLOB)))
                FROM recorded_marks m WHERE m.session_id = s.id), 0)
            + COALESCE((
                SELECT SUM(length(CAST(
                    endpoint.session_id || endpoint.role || endpoint.execution_id
                    || endpoint.table_ordinal || endpoint.row_ordinal || endpoint.column_ordinal
                    AS BLOB)))
                FROM recorded_chain_endpoints endpoint WHERE endpoint.session_id = s.id), 0)
                AS stored_bytes
        FROM recorded_sessions s
        """;

    private readonly string databaseDirectory;
    private readonly string databasePath;
    private readonly string connectionString;
    private readonly long maximumDatabaseBytes;
    private readonly int maximumExecutionsPerSession;
    private readonly int maximumResultBytesPerExecution;
    private readonly int maximumSessionCount;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private Lazy<Task> initializationTask;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteKustoRecordedSessionStore"/> class using local application data.
    /// </summary>
    public SqliteKustoRecordedSessionStore()
        : this(GetDefaultFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteKustoRecordedSessionStore"/> class.
    /// </summary>
    /// <param name="filePath">The absolute SQLite database path.</param>
    public SqliteKustoRecordedSessionStore(string filePath)
        : this(
            filePath,
            MaximumSessionCount,
            MaximumExecutionsPerSession,
            MaximumDatabaseBytes,
            MaximumRecordedResultBytesPerExecution)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteKustoRecordedSessionStore"/> class with explicit limits.
    /// </summary>
    /// <param name="filePath">The absolute SQLite database path.</param>
    /// <param name="maximumSessionCount">The maximum retained sessions.</param>
    /// <param name="maximumExecutionsPerSession">The maximum executions in one session.</param>
    /// <param name="maximumDatabaseBytes">The maximum logical database size.</param>
    /// <param name="maximumResultBytesPerExecution">The maximum estimated result bytes per execution.</param>
    internal SqliteKustoRecordedSessionStore(
        string filePath,
        int maximumSessionCount,
        int maximumExecutionsPerSession,
        long maximumDatabaseBytes,
        int maximumResultBytesPerExecution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSessionCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExecutionsPerSession);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDatabaseBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResultBytesPerExecution);
        string fullPath = Path.GetFullPath(filePath);
        databasePath = fullPath;
        databaseDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The recorded-session database path has no directory.");
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
        this.maximumSessionCount = maximumSessionCount;
        this.maximumExecutionsPerSession = maximumExecutionsPerSession;
        this.maximumDatabaseBytes = maximumDatabaseBytes;
        this.maximumResultBytesPerExecution = maximumResultBytesPerExecution;
        initializationTask = new Lazy<Task>(InitializeAsync);
    }

    /// <inheritdoc />
    public string DatabaseDirectoryPath => databaseDirectory;

    /// <inheritdoc />
    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(
                () =>
                {
                    SqliteConnection.ClearAllPools();
                    DeleteDatabaseFile(databasePath);
                    DeleteDatabaseFile(databasePath + "-wal");
                    DeleteDatabaseFile(databasePath + "-shm");
                },
                cancellationToken).ConfigureAwait(false);
            Lazy<Task> replacementInitialization = new(InitializeAsync);
            Volatile.Write(ref initializationTask, replacementInitialization);
            await replacementInitialization.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<KustoRecordingPeriod> CreateSessionAsync(
        string name,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalizedName = NormalizeName(name);
        Guid sessionId = Guid.NewGuid();
        Guid periodId = Guid.NewGuid();
        DateTimeOffset utcStart = startedAtUtc.ToUniversalTime();
        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                EnsureDatabaseCapacity(connection, transaction);
                EnsureSessionCapacity(connection, transaction);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO recorded_sessions (id, name, normalized_name, created_at_utc, last_updated_at_utc)
                    VALUES ($sessionId, $name, $normalizedName, $started, $started);
                    INSERT INTO recording_periods (id, session_id, started_at_utc, stopped_at_utc)
                    VALUES ($periodId, $sessionId, $started, NULL);
                    """;
                command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
                command.Parameters.AddWithValue("$periodId", FormatGuid(periodId));
                command.Parameters.AddWithValue("$name", name.Trim());
                command.Parameters.AddWithValue("$normalizedName", normalizedName);
                command.Parameters.AddWithValue("$started", FormatTimestamp(utcStart));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return new KustoRecordingPeriod(periodId, sessionId, utcStart, null);
    }

    /// <inheritdoc />
    public async Task<KustoRecordingPeriod> AppendSessionAsync(
        Guid sessionId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        Guid periodId = Guid.NewGuid();
        DateTimeOffset utcStart = startedAtUtc.ToUniversalTime();
        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                EnsureSessionExists(connection, transaction, sessionId);
                EnsureNoOpenPeriod(connection, transaction);
                EnsureDatabaseCapacity(connection, transaction);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO recording_periods (id, session_id, started_at_utc, stopped_at_utc)
                    VALUES ($periodId, $sessionId, $started, NULL);
                    UPDATE recorded_sessions SET last_updated_at_utc = $started WHERE id = $sessionId;
                    """;
                command.Parameters.AddWithValue("$periodId", FormatGuid(periodId));
                command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
                command.Parameters.AddWithValue("$started", FormatTimestamp(utcStart));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return new KustoRecordingPeriod(periodId, sessionId, utcStart, null);
    }

    /// <inheritdoc />
    public async Task StopRecordingAsync(
        Guid periodId,
        DateTimeOffset stoppedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(periodId, Guid.Empty);
        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE recording_periods
                    SET stopped_at_utc = $stopped
                    WHERE id = $periodId AND stopped_at_utc IS NULL;
                    UPDATE recorded_sessions
                    SET last_updated_at_utc = $stopped
                    WHERE id = (SELECT session_id FROM recording_periods WHERE id = $periodId);
                    """;
                command.Parameters.AddWithValue("$periodId", FormatGuid(periodId));
                command.Parameters.AddWithValue("$stopped", FormatTimestamp(stoppedAtUtc));
                int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (affected == 0)
                {
                    throw new InvalidOperationException("The recording period does not exist or is already stopped.");
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Guid> BeginExecutionAsync(
        KustoRecordedExecutionStart execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (Encoding.UTF8.GetByteCount(execution.Request.QueryText) > MaximumRecordedQueryBytes)
        {
            throw new InvalidOperationException(
                $"Recorded query text exceeds the {MaximumRecordedQueryBytes / 1024:N0} KiB limit.");
        }

        Guid executionId = Guid.NewGuid();
        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                Guid sessionId = ReadOpenPeriodSessionId(connection, transaction, execution.PeriodId);
                EnsureDatabaseCapacity(connection, transaction);
                EnsureExecutionCapacity(connection, transaction, sessionId);
                long sequence = ReadNextSequence(connection, transaction, sessionId);
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO recorded_executions (
                            id, session_id, period_id, sequence, document_id, document_title,
                            cluster_uri, database_name, query_text, started_at_utc, completed_at_utc,
                            status, error_message, duration_milliseconds, completeness,
                            source_table_name, is_composable)
                        VALUES (
                            $id, $sessionId, $periodId, $sequence, $documentId, $documentTitle,
                            $clusterUri, $databaseName, $queryText, $started, NULL,
                            $status, NULL, NULL, $completeness, $sourceTableName, $isComposable);
                        UPDATE recorded_sessions SET last_updated_at_utc = $started WHERE id = $sessionId;
                        """;
                    command.Parameters.AddWithValue("$id", FormatGuid(executionId));
                    command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
                    command.Parameters.AddWithValue("$periodId", FormatGuid(execution.PeriodId));
                    command.Parameters.AddWithValue("$sequence", sequence);
                    command.Parameters.AddWithValue("$documentId", FormatGuid(execution.DocumentId));
                    command.Parameters.AddWithValue("$documentTitle", execution.DocumentTitle);
                    command.Parameters.AddWithValue("$clusterUri", execution.Request.ClusterUri.AbsoluteUri);
                    command.Parameters.AddWithValue("$databaseName", execution.Request.DatabaseName);
                    command.Parameters.AddWithValue("$queryText", execution.Request.QueryText);
                    command.Parameters.AddWithValue("$started", FormatTimestamp(execution.StartedAtUtc));
                    command.Parameters.AddWithValue("$status", (int)KustoRecordedExecutionStatus.Running);
                    command.Parameters.AddWithValue("$completeness", (int)KustoQueryResultCompleteness.Complete);
                    command.Parameters.AddWithValue(
                        "$sourceTableName",
                        (object?)execution.Relation?.SourceTableName ?? DBNull.Value);
                    command.Parameters.AddWithValue("$isComposable", execution.Relation?.IsComposable == true ? 1 : 0);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                WriteRelationColumns(connection, transaction, executionId, execution.Relation);
                WritePredicateInterests(
                    connection,
                    transaction,
                    sessionId,
                    executionId,
                    execution.PredicateInterests);
            },
            cancellationToken).ConfigureAwait(false);
        return executionId;
    }

    /// <inheritdoc />
    public async Task CompleteExecutionAsync(
        Guid executionId,
        KustoRecordedExecutionCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(completion);
        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                (Guid SessionId, DateTimeOffset StartedAtUtc) execution = ReadRunningExecution(
                    connection,
                    transaction,
                    executionId);
                KustoQueryResult? recordedResult = completion.Result is null
                    ? null
                    : LimitRecordedResult(
                        completion.Result,
                        GetAvailableResultBytes(connection, transaction));
                using (SqliteCommand command = connection.CreateCommand())
                {
                    double durationMilliseconds = recordedResult?.Duration.TotalMilliseconds
                        ?? (completion.CompletedAtUtc - execution.StartedAtUtc).TotalMilliseconds;
                    command.Transaction = transaction;
                    command.CommandText = """
                        UPDATE recorded_executions
                        SET completed_at_utc = $completed,
                            status = $status,
                            error_message = $error,
                            duration_milliseconds = $duration,
                            completeness = $completeness
                        WHERE id = $id AND status = $running;
                        UPDATE recorded_sessions SET last_updated_at_utc = $completed WHERE id = $sessionId;
                        """;
                    command.Parameters.AddWithValue("$id", FormatGuid(executionId));
                    command.Parameters.AddWithValue("$sessionId", FormatGuid(execution.SessionId));
                    command.Parameters.AddWithValue("$completed", FormatTimestamp(completion.CompletedAtUtc));
                    command.Parameters.AddWithValue("$status", (int)completion.Status);
                    command.Parameters.AddWithValue("$running", (int)KustoRecordedExecutionStatus.Running);
                    command.Parameters.AddWithValue("$error", (object?)completion.ErrorMessage ?? DBNull.Value);
                    command.Parameters.AddWithValue("$duration", durationMilliseconds);
                    command.Parameters.AddWithValue(
                        "$completeness",
                        (int)(recordedResult?.Completeness ?? KustoQueryResultCompleteness.Complete));
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (recordedResult is not null)
                {
                    WriteResult(connection, transaction, executionId, recordedResult);
                    WriteConfirmedPredicateMarks(
                        connection,
                        transaction,
                        execution.SessionId,
                        executionId,
                        completion.CompletedAtUtc);
                }
            },
            cancellationToken).ConfigureAwait(false);
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
    public async Task<IReadOnlyList<KustoRecordedMark>> AddMarksAsync(
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
            .DistinctBy(coordinate => (
                coordinate.ExecutionId,
                coordinate.TableOrdinal,
                coordinate.RowOrdinal,
                coordinate.ColumnOrdinal))
            .ToArray();
        List<KustoRecordedMark> marks = [];
        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                foreach (KustoRecordedValueCoordinate coordinate in distinctCoordinates)
                {
                    EnsureCoordinateExists(connection, transaction, sessionId, coordinate);
                    marks.Add(WriteMark(
                        connection,
                        transaction,
                        sessionId,
                        kind,
                        coordinate,
                        createdAtUtc,
                        KustoRecordedInterestSource.ManualCell));
                }

                await Task.CompletedTask.ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return marks.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task RemoveMarkAsync(Guid markId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(markId, Guid.Empty);
        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    DELETE FROM recorded_value_interests WHERE mark_id = $markId;
                    DELETE FROM recorded_marks WHERE id = $markId;
                    """;
                command.Parameters.AddWithValue("$markId", FormatGuid(markId));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetEndpointAsync(
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

        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                EnsureCoordinateExists(connection, transaction, sessionId, coordinate);
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO recorded_chain_endpoints (
                        session_id, role, execution_id, table_ordinal, row_ordinal, column_ordinal)
                    VALUES ($sessionId, $role, $executionId, $tableOrdinal, $rowOrdinal, $columnOrdinal)
                    ON CONFLICT(session_id, role) DO UPDATE SET
                        execution_id = excluded.execution_id,
                        table_ordinal = excluded.table_ordinal,
                        row_ordinal = excluded.row_ordinal,
                        column_ordinal = excluded.column_ordinal;
                    """;
                AddCoordinateParameters(command, sessionId, coordinate);
                command.Parameters.AddWithValue("$role", (int)role);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ClearEndpointAsync(
        Guid sessionId,
        KustoChainEndpointRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM recorded_chain_endpoints WHERE session_id = $sessionId AND role = $role;";
                command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
                command.Parameters.AddWithValue("$role", (int)role);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KustoRecordedSessionSummary>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        using KustoPerformanceTrace.OperationScope measurement = KustoPerformanceTrace.Measure(
            "sessions.catalog.load");
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                async () =>
                {
                    using SqliteConnection connection = OpenConnection();
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = $"{SelectSessionSummariesSql} ORDER BY s.last_updated_at_utc DESC, s.name;";
                    await using SqliteDataReader reader = await command
                        .ExecuteReaderAsync(cancellationToken)
                        .ConfigureAwait(false);
                    List<KustoRecordedSessionSummary> summaries = [];
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        summaries.Add(ReadSessionSummary(reader));
                    }

                    return summaries.AsReadOnly();
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KustoRecordedInterest>> GetInterestsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () =>
                {
                    using SqliteConnection connection = OpenConnection();
                    return (IReadOnlyList<KustoRecordedInterest>)ReadInterests(connection, sessionId);
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<KustoRecordedSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        using KustoPerformanceTrace.OperationScope measurement = KustoPerformanceTrace.Measure(
            "sessions.aggregate.load");
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () =>
                {
                    using SqliteConnection connection = OpenConnection();
                    KustoRecordedSessionSummary? summary = ReadSessionSummary(connection, sessionId);
                    if (summary is null)
                    {
                        return null;
                    }

                    return new KustoRecordedSession(
                        summary,
                        ReadPeriods(connection, sessionId),
                        ReadExecutions(connection, sessionId),
                        ReadInterests(connection, sessionId),
                        ReadMarks(connection, sessionId),
                        ReadEndpoints(connection, sessionId));
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM recorded_sessions WHERE id = $sessionId;";
                command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RenameExecutionAsync(
        Guid executionId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string normalizedDisplayName = displayName.Trim();
        if (normalizedDisplayName.Length > 80)
        {
            throw new ArgumentException("The recorded query name cannot exceed 80 characters.", nameof(displayName));
        }

        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE recorded_executions
                    SET display_name = $displayName
                    WHERE id = $executionId;
                    UPDATE recorded_sessions
                    SET last_updated_at_utc = $updated
                    WHERE id = (SELECT session_id FROM recorded_executions WHERE id = $executionId);
                    """;
                command.Parameters.AddWithValue("$displayName", normalizedDisplayName);
                command.Parameters.AddWithValue("$executionId", FormatGuid(executionId));
                command.Parameters.AddWithValue("$updated", FormatTimestamp(DateTimeOffset.UtcNow));
                int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (affected == 0)
                {
                    throw new InvalidOperationException("The recorded query does not exist.");
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteExecutionAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        await ExecuteWriteAsync(
            async (connection, transaction) =>
            {
                Guid? sessionId = null;
                using (SqliteCommand lookupCommand = connection.CreateCommand())
                {
                    lookupCommand.Transaction = transaction;
                    lookupCommand.CommandText = "SELECT session_id, status FROM recorded_executions WHERE id = $id;";
                    lookupCommand.Parameters.AddWithValue("$id", FormatGuid(executionId));
                    await using SqliteDataReader reader = await lookupCommand
                        .ExecuteReaderAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if ((KustoRecordedExecutionStatus)reader.GetInt32(1)
                            == KustoRecordedExecutionStatus.Running)
                        {
                            throw new InvalidOperationException("A running recorded query cannot be deleted.");
                        }

                        sessionId = Guid.Parse(reader.GetString(0));
                    }
                }

                if (sessionId is Guid owningSessionId)
                {
                    using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
                        DELETE FROM recorded_executions WHERE id = $id;
                        UPDATE recorded_sessions
                        SET last_updated_at_utc = $updated
                        WHERE id = $sessionId;
                        """;
                    command.Parameters.AddWithValue("$id", FormatGuid(executionId));
                    command.Parameters.AddWithValue("$updated", FormatTimestamp(DateTimeOffset.UtcNow));
                    command.Parameters.AddWithValue("$sessionId", FormatGuid(owningSessionId));
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
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

    private static KustoQueryResult LimitRecordedResult(KustoQueryResult result, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        int remainingRows = MaximumRecordedRowsPerExecution;
        long remainingBytes = maximumBytes;
        bool wasTruncated = false;
        bool capacityExhausted = false;
        List<KustoResultTable> tables = [];
        foreach (KustoResultTable table in result.Tables)
        {
            List<KustoResultRow> retainedRows = [];
            foreach (KustoResultRow row in table.Rows)
            {
                long estimatedBytes = EstimateRecordedRowBytes(table.Columns, row);
                if (capacityExhausted || remainingRows == 0 || estimatedBytes > remainingBytes)
                {
                    capacityExhausted = true;
                    wasTruncated = true;
                    break;
                }

                retainedRows.Add(row);
                remainingRows--;
                remainingBytes -= estimatedBytes;
            }

            wasTruncated |= retainedRows.Count != table.Rows.Count;
            tables.Add(new KustoResultTable(
                table.Name,
                table.Columns,
                retainedRows));
        }

        return wasTruncated
            ? new KustoQueryResult(
                tables,
                result.Duration,
                result.Visualization,
                KustoQueryResultCompleteness.RecordLimitReached)
            : result;
    }

    private static long EstimateRecordedRowBytes(
        IReadOnlyList<KustoResultColumn> columns,
        KustoResultRow row)
    {
        const int FixedRowBytes = 256;
        long estimatedBytes = FixedRowBytes;
        for (int index = 0; index < row.ResultValues.Count; index++)
        {
            KustoResultValue value = row.ResultValues[index];
            int displayBytes = Encoding.UTF8.GetByteCount(value.DisplayText);
            int rawBytes = value.RawJson is null
                ? displayBytes
                : Encoding.UTF8.GetByteCount(value.RawJson);
            estimatedBytes += (displayBytes * 3L) + (rawBytes * 2L) + 128;

            if (index < columns.Count)
            {
                estimatedBytes += Encoding.UTF8.GetByteCount(columns[index].Name);
                estimatedBytes += Encoding.UTF8.GetByteCount(columns[index].TypeName);
            }
        }

        return estimatedBytes;
    }

    private static string GetDefaultFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenKustoExplorer",
            "recorded-sessions.db");
    }

    private static void DeleteDatabaseFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static long ReadDatabaseUsedBytes(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA page_count;";
        long pageCount = (long)(command.ExecuteScalar() ?? 0L);
        command.CommandText = "PRAGMA freelist_count;";
        long freePageCount = (long)(command.ExecuteScalar() ?? 0L);
        command.CommandText = "PRAGMA page_size;";
        long pageSize = (long)(command.ExecuteScalar() ?? 0L);
        return Math.Max(0, pageCount - freePageCount) * pageSize;
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().ToUpperInvariant();
    }

    private static string FormatGuid(Guid value)
    {
        return value.ToString("D");
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static void RecoverInterruptedRecordings(SqliteConnection connection, DateTimeOffset recoveredAtUtc)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE recorded_executions
            SET status = $interrupted, completed_at_utc = $recovered,
                error_message = 'Recording interrupted when the application stopped.'
            WHERE status = $running;
            UPDATE recording_periods SET stopped_at_utc = $recovered WHERE stopped_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$interrupted", (int)KustoRecordedExecutionStatus.Interrupted);
        command.Parameters.AddWithValue("$running", (int)KustoRecordedExecutionStatus.Running);
        command.Parameters.AddWithValue("$recovered", FormatTimestamp(recoveredAtUtc));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void EnsureSessionExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM recorded_sessions WHERE id = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        if ((long)(command.ExecuteScalar() ?? 0L) == 0)
        {
            throw new InvalidOperationException("The recorded session does not exist.");
        }
    }

    private static void EnsureNoOpenPeriod(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM recording_periods WHERE stopped_at_utc IS NULL;";
        if ((long)(command.ExecuteScalar() ?? 0L) > 0)
        {
            throw new InvalidOperationException("Another recording period is already active.");
        }
    }

    private static Guid ReadOpenPeriodSessionId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid periodId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT session_id FROM recording_periods WHERE id = $periodId AND stopped_at_utc IS NULL;";
        command.Parameters.AddWithValue("$periodId", FormatGuid(periodId));
        string? value = command.ExecuteScalar() as string;
        return value is null
            ? throw new InvalidOperationException("The recording period does not exist or is already stopped.")
            : Guid.Parse(value);
    }

    private static long ReadNextSequence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(sequence), 0) + 1 FROM recorded_executions WHERE session_id = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        return (long)(command.ExecuteScalar() ?? 1L);
    }

    private static void WriteRelationColumns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid executionId,
        KustoRecordedRelationDescriptor? relation)
    {
        if (relation is null)
        {
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO recorded_relation_columns (
                execution_id, ordinal, result_column_name, source_column_name)
            VALUES ($executionId, $ordinal, $resultColumnName, $sourceColumnName);
            """;
        command.Parameters.AddWithValue("$executionId", FormatGuid(executionId));
        command.Parameters.Add("$ordinal", SqliteType.Integer);
        command.Parameters.Add("$resultColumnName", SqliteType.Text);
        command.Parameters.Add("$sourceColumnName", SqliteType.Text);
        for (int index = 0; index < relation.Columns.Count; index++)
        {
            command.Parameters["$ordinal"].Value = index;
            command.Parameters["$resultColumnName"].Value = relation.Columns[index].ResultColumnName;
            command.Parameters["$sourceColumnName"].Value = relation.Columns[index].SourceColumnName;
            command.ExecuteNonQuery();
        }
    }

    private static void WritePredicateInterests(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        Guid executionId,
        IReadOnlyList<KustoPredicateInterest> interests)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO recorded_value_interests (
                id, session_id, declared_execution_id, source, column_name,
                type_name, canonical_value, value_hash, coordinate_execution_id,
                table_ordinal, row_ordinal, column_ordinal, literal_start,
                literal_length, mark_id, is_suppressed)
            VALUES (
                $id, $sessionId, $executionId, $source, $columnName,
                $typeName, $canonicalValue, $valueHash, NULL,
                NULL, NULL, NULL, $literalStart, $literalLength, NULL, 0);
            """;
        command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        command.Parameters.AddWithValue("$executionId", FormatGuid(executionId));
        command.Parameters.AddWithValue("$source", (int)KustoRecordedInterestSource.QueryPredicate);
        command.Parameters.Add("$columnName", SqliteType.Text);
        command.Parameters.Add("$typeName", SqliteType.Text);
        command.Parameters.Add("$canonicalValue", SqliteType.Text);
        command.Parameters.Add("$valueHash", SqliteType.Text);
        command.Parameters.Add("$literalStart", SqliteType.Integer);
        command.Parameters.Add("$literalLength", SqliteType.Integer);
        foreach (KustoPredicateInterest interest in interests)
        {
            KustoRecordedValueIdentity identity = new(interest.TypeName, interest.Value, false);
            command.Parameters["$id"].Value = FormatGuid(Guid.NewGuid());
            command.Parameters["$columnName"].Value = interest.ColumnName;
            command.Parameters["$typeName"].Value = identity.TypeName;
            command.Parameters["$canonicalValue"].Value = identity.CanonicalValue;
            command.Parameters["$valueHash"].Value = KustoRecordedValueCanonicalizer.CreateHash(identity);
            command.Parameters["$literalStart"].Value = interest.LiteralStart;
            command.Parameters["$literalLength"].Value = interest.LiteralLength;
            command.ExecuteNonQuery();
        }
    }

    private static (Guid SessionId, DateTimeOffset StartedAtUtc) ReadRunningExecution(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid executionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT session_id, started_at_utc FROM recorded_executions WHERE id = $id AND status = $running;";
        command.Parameters.AddWithValue("$id", FormatGuid(executionId));
        command.Parameters.AddWithValue("$running", (int)KustoRecordedExecutionStatus.Running);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("The recorded execution is not running.");
        }

        return (Guid.Parse(reader.GetString(0)), ParseTimestamp(reader.GetString(1)));
    }

    private static void WriteResult(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid executionId,
        KustoQueryResult result)
    {
        for (int tableOrdinal = 0; tableOrdinal < result.Tables.Count; tableOrdinal++)
        {
            KustoResultTable table = result.Tables[tableOrdinal];
            Guid tableId = Guid.NewGuid();
            using (SqliteCommand tableCommand = connection.CreateCommand())
            {
                tableCommand.Transaction = transaction;
                tableCommand.CommandText = """
                    INSERT INTO recorded_result_tables (id, execution_id, ordinal, name)
                    VALUES ($id, $executionId, $ordinal, $name);
                    """;
                tableCommand.Parameters.AddWithValue("$id", FormatGuid(tableId));
                tableCommand.Parameters.AddWithValue("$executionId", FormatGuid(executionId));
                tableCommand.Parameters.AddWithValue("$ordinal", tableOrdinal);
                tableCommand.Parameters.AddWithValue("$name", table.Name);
                tableCommand.ExecuteNonQuery();
            }

            WriteColumns(connection, transaction, tableId, table.Columns);
            WriteRows(connection, transaction, executionId, tableId, tableOrdinal, table);
        }
    }

    private static void WriteColumns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid tableId,
        IReadOnlyList<KustoResultColumn> columns)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO recorded_result_columns (table_id, ordinal, name, type_name)
            VALUES ($tableId, $ordinal, $name, $typeName);
            """;
        command.Parameters.AddWithValue("$tableId", FormatGuid(tableId));
        command.Parameters.Add("$ordinal", SqliteType.Integer);
        command.Parameters.Add("$name", SqliteType.Text);
        command.Parameters.Add("$typeName", SqliteType.Text);
        for (int index = 0; index < columns.Count; index++)
        {
            command.Parameters["$ordinal"].Value = index;
            command.Parameters["$name"].Value = columns[index].Name;
            command.Parameters["$typeName"].Value = columns[index].TypeName;
            command.ExecuteNonQuery();
        }
    }

    private static void WriteRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid executionId,
        Guid tableId,
        int tableOrdinal,
        KustoResultTable table)
    {
        for (int rowOrdinal = 0; rowOrdinal < table.Rows.Count; rowOrdinal++)
        {
            KustoResultRow row = table.Rows[rowOrdinal];
            Guid rowId = Guid.NewGuid();
            string displayJson = SerializeStrings(row.Values);
            string rawJson = SerializeRawValues(row.ResultValues);
            List<KustoRecordedValueIdentity> identities = [];
            for (int columnOrdinal = 0; columnOrdinal < table.Columns.Count; columnOrdinal++)
            {
                identities.Add(KustoRecordedValueCanonicalizer.Create(
                    table.Columns[columnOrdinal].TypeName,
                    row.ResultValues[columnOrdinal]));
            }

            string rowKey = KustoRecordedRowCanonicalizer.CreateKey(table.Columns, row);
            using (SqliteCommand rowCommand = connection.CreateCommand())
            {
                rowCommand.Transaction = transaction;
                rowCommand.CommandText = """
                    INSERT INTO recorded_result_rows (
                        id, table_id, ordinal, display_values_json, raw_values_json, row_key)
                    VALUES ($id, $tableId, $ordinal, $displayJson, $rawJson, $rowKey);
                    """;
                rowCommand.Parameters.AddWithValue("$id", FormatGuid(rowId));
                rowCommand.Parameters.AddWithValue("$tableId", FormatGuid(tableId));
                rowCommand.Parameters.AddWithValue("$ordinal", rowOrdinal);
                rowCommand.Parameters.AddWithValue("$displayJson", displayJson);
                rowCommand.Parameters.AddWithValue("$rawJson", rawJson);
                rowCommand.Parameters.AddWithValue("$rowKey", rowKey);
                rowCommand.ExecuteNonQuery();
            }

            WriteOccurrences(
                connection,
                transaction,
                executionId,
                tableId,
                rowId,
                tableOrdinal,
                rowOrdinal,
                table,
                identities);
        }
    }

    private static void WriteOccurrences(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid executionId,
        Guid tableId,
        Guid rowId,
        int tableOrdinal,
        int rowOrdinal,
        KustoResultTable table,
        List<KustoRecordedValueIdentity> identities)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO recorded_value_occurrences (
                execution_id, table_id, row_id, table_ordinal, row_ordinal,
                column_ordinal, column_name, type_name, canonical_value,
                value_hash, display_text, is_null)
            VALUES (
                $executionId, $tableId, $rowId, $tableOrdinal, $rowOrdinal,
                $columnOrdinal, $columnName, $typeName, $canonicalValue,
                $valueHash, $displayText, $isNull);
            """;
        command.Parameters.AddWithValue("$executionId", FormatGuid(executionId));
        command.Parameters.AddWithValue("$tableId", FormatGuid(tableId));
        command.Parameters.AddWithValue("$rowId", FormatGuid(rowId));
        command.Parameters.AddWithValue("$tableOrdinal", tableOrdinal);
        command.Parameters.AddWithValue("$rowOrdinal", rowOrdinal);
        command.Parameters.Add("$columnOrdinal", SqliteType.Integer);
        command.Parameters.Add("$columnName", SqliteType.Text);
        command.Parameters.Add("$typeName", SqliteType.Text);
        command.Parameters.Add("$canonicalValue", SqliteType.Text);
        command.Parameters.Add("$valueHash", SqliteType.Text);
        command.Parameters.Add("$displayText", SqliteType.Text);
        command.Parameters.Add("$isNull", SqliteType.Integer);
        for (int columnOrdinal = 0; columnOrdinal < identities.Count; columnOrdinal++)
        {
            KustoRecordedValueIdentity identity = identities[columnOrdinal];
            command.Parameters["$columnOrdinal"].Value = columnOrdinal;
            command.Parameters["$columnName"].Value = table.Columns[columnOrdinal].Name;
            command.Parameters["$typeName"].Value = identity.TypeName;
            command.Parameters["$canonicalValue"].Value = identity.CanonicalValue;
            command.Parameters["$valueHash"].Value = KustoRecordedValueCanonicalizer.CreateHash(identity);
            command.Parameters["$displayText"].Value = table.Rows[rowOrdinal].Values[columnOrdinal];
            command.Parameters["$isNull"].Value = identity.IsNull ? 1 : 0;
            command.ExecuteNonQuery();
        }
    }

    private static void WriteConfirmedPredicateMarks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        Guid executionId,
        DateTimeOffset createdAtUtc)
    {
        List<KustoRecordedValueCoordinate> coordinates = [];
        HashSet<Guid> matchedInterests = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT i.id, o.table_ordinal, o.row_ordinal, o.column_ordinal
                FROM recorded_value_interests i
                JOIN recorded_value_occurrences o
                  ON o.execution_id = i.declared_execution_id
                 AND o.value_hash = i.value_hash
                 AND o.type_name = i.type_name
                 AND o.canonical_value = i.canonical_value
                 AND o.is_null = 0
                WHERE i.declared_execution_id = $executionId
                  AND i.source = $source
                  AND i.is_suppressed = 0
                  AND (
                    o.column_name = i.column_name COLLATE NOCASE
                    OR EXISTS (
                      SELECT 1
                      FROM recorded_relation_columns c
                      WHERE c.execution_id = o.execution_id
                        AND c.result_column_name = o.column_name COLLATE NOCASE
                        AND c.source_column_name = i.column_name COLLATE NOCASE))
                ORDER BY i.id, o.table_ordinal, o.row_ordinal, o.column_ordinal;
                """;
            command.Parameters.AddWithValue("$executionId", FormatGuid(executionId));
            command.Parameters.AddWithValue("$source", (int)KustoRecordedInterestSource.QueryPredicate);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                Guid interestId = Guid.Parse(reader.GetString(0));
                if (matchedInterests.Add(interestId))
                {
                    coordinates.Add(new KustoRecordedValueCoordinate(
                        executionId,
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        reader.GetInt32(3)));
                }
            }
        }

        foreach (KustoRecordedValueCoordinate coordinate in coordinates)
        {
            WriteMark(
                connection,
                transaction,
                sessionId,
                KustoRecordedMarkKind.Cell,
                coordinate,
                createdAtUtc,
                KustoRecordedInterestSource.ConfirmedPredicate);
        }
    }

    private static string SerializeStrings(IReadOnlyList<string> values)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartArray();
            foreach (string value in values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string SerializeRawValues(IReadOnlyList<KustoResultValue> values)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartArray();
            foreach (string? rawJson in values.Select(value => value.RawJson))
            {
                if (rawJson is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    writer.WriteStringValue(rawJson);
                }
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void EnsureCoordinateExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        KustoRecordedValueCoordinate coordinate)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM recorded_value_occurrences o
            JOIN recorded_executions e ON e.id = o.execution_id
            WHERE e.session_id = $sessionId
              AND o.execution_id = $executionId
              AND o.table_ordinal = $tableOrdinal
              AND o.row_ordinal = $rowOrdinal
              AND o.column_ordinal = $columnOrdinal;
            """;
        AddCoordinateParameters(command, sessionId, coordinate);
        if ((long)(command.ExecuteScalar() ?? 0L) == 0)
        {
            throw new InvalidOperationException("The recorded result coordinate does not exist in the session.");
        }
    }

    private static void AddCoordinateParameters(
        SqliteCommand command,
        Guid sessionId,
        KustoRecordedValueCoordinate coordinate)
    {
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        command.Parameters.AddWithValue("$executionId", FormatGuid(coordinate.ExecutionId));
        command.Parameters.AddWithValue("$tableOrdinal", coordinate.TableOrdinal);
        command.Parameters.AddWithValue("$rowOrdinal", coordinate.RowOrdinal);
        command.Parameters.AddWithValue("$columnOrdinal", coordinate.ColumnOrdinal);
    }

    private static KustoRecordedMark? ReadMark(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        KustoRecordedMarkKind kind,
        KustoRecordedValueCoordinate coordinate)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, created_at_utc
            FROM recorded_marks
            WHERE session_id = $sessionId AND kind = $kind AND execution_id = $executionId
              AND table_ordinal = $tableOrdinal AND row_ordinal = $rowOrdinal
              AND column_ordinal = $columnOrdinal;
            """;
        AddCoordinateParameters(command, sessionId, coordinate);
        command.Parameters.AddWithValue("$kind", (int)kind);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new KustoRecordedMark(
                Guid.Parse(reader.GetString(0)),
                sessionId,
                kind,
                coordinate,
                ParseTimestamp(reader.GetString(1)))
            : null;
    }

    private static KustoRecordedMark WriteMark(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        KustoRecordedMarkKind kind,
        KustoRecordedValueCoordinate coordinate,
        DateTimeOffset createdAtUtc,
        KustoRecordedInterestSource? interestSource)
    {
        KustoRecordedMark? existing = ReadMark(connection, transaction, sessionId, kind, coordinate);
        if (existing is not null)
        {
            return existing;
        }

        Guid markId = Guid.NewGuid();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO recorded_marks (
                    id, session_id, kind, execution_id, table_ordinal,
                    row_ordinal, column_ordinal, created_at_utc)
                VALUES (
                    $id, $sessionId, $kind, $executionId, $tableOrdinal,
                    $rowOrdinal, $columnOrdinal, $createdAtUtc);
                """;
            AddCoordinateParameters(command, sessionId, coordinate);
            command.Parameters.AddWithValue("$id", FormatGuid(markId));
            command.Parameters.AddWithValue("$kind", (int)kind);
            command.Parameters.AddWithValue("$createdAtUtc", FormatTimestamp(createdAtUtc));
            command.ExecuteNonQuery();
        }

        KustoRecordedMark mark = new(markId, sessionId, kind, coordinate, createdAtUtc);
        if (interestSource is not null)
        {
            WriteMarkedInterest(connection, transaction, mark, interestSource.Value);
        }

        return mark;
    }

    private static void WriteMarkedInterest(
        SqliteConnection connection,
        SqliteTransaction transaction,
        KustoRecordedMark mark,
        KustoRecordedInterestSource source)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO recorded_value_interests (
                id, session_id, declared_execution_id, source, column_name,
                type_name, canonical_value, value_hash, coordinate_execution_id,
                table_ordinal, row_ordinal, column_ordinal, literal_start,
                literal_length, mark_id, is_suppressed)
            SELECT
                $id, $sessionId, o.execution_id, $source, o.column_name,
                o.type_name, o.canonical_value, o.value_hash, o.execution_id,
                o.table_ordinal, o.row_ordinal, o.column_ordinal, NULL,
                NULL, $markId, 0
            FROM recorded_value_occurrences o
            WHERE o.execution_id = $executionId AND o.table_ordinal = $tableOrdinal
              AND o.row_ordinal = $rowOrdinal AND o.column_ordinal = $columnOrdinal;
            """;
        command.Parameters.AddWithValue("$id", FormatGuid(Guid.NewGuid()));
        command.Parameters.AddWithValue("$sessionId", FormatGuid(mark.SessionId));
        command.Parameters.AddWithValue("$source", (int)source);
        command.Parameters.AddWithValue("$markId", FormatGuid(mark.Id));
        command.Parameters.AddWithValue("$executionId", FormatGuid(mark.Coordinate.ExecutionId));
        command.Parameters.AddWithValue("$tableOrdinal", mark.Coordinate.TableOrdinal);
        command.Parameters.AddWithValue("$rowOrdinal", mark.Coordinate.RowOrdinal);
        command.Parameters.AddWithValue("$columnOrdinal", mark.Coordinate.ColumnOrdinal);
        command.ExecuteNonQuery();
    }

    private static KustoRecordedSessionSummary? ReadSessionSummary(SqliteConnection connection, Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{SelectSessionSummariesSql} WHERE s.id = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadSessionSummary(reader) : null;
    }

    private static KustoRecordedSessionSummary ReadSessionSummary(SqliteDataReader reader)
    {
        return new KustoRecordedSessionSummary(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            ParseTimestamp(reader.GetString(2)),
            ParseTimestamp(reader.GetString(3)),
            reader.GetInt32(4),
            reader.GetInt64(5));
    }

    private static ReadOnlyCollection<KustoRecordingPeriod> ReadPeriods(SqliteConnection connection, Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, started_at_utc, stopped_at_utc
            FROM recording_periods WHERE session_id = $sessionId ORDER BY started_at_utc;
            """;
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        using SqliteDataReader reader = command.ExecuteReader();
        List<KustoRecordingPeriod> periods = [];
        while (reader.Read())
        {
            periods.Add(new KustoRecordingPeriod(
                Guid.Parse(reader.GetString(0)),
                sessionId,
                ParseTimestamp(reader.GetString(1)),
                reader.IsDBNull(2) ? null : ParseTimestamp(reader.GetString(2))));
        }

        return periods.AsReadOnly();
    }

    private static ReadOnlyCollection<KustoRecordedExecution> ReadExecutions(SqliteConnection connection, Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, period_id, sequence, document_id, document_title, cluster_uri,
                   database_name, query_text, started_at_utc, completed_at_utc,
                   status, error_message, duration_milliseconds, completeness,
                     source_table_name, is_composable, display_name
            FROM recorded_executions
            WHERE session_id = $sessionId ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        using SqliteDataReader reader = command.ExecuteReader();
        List<KustoRecordedExecution> executions = [];
        while (reader.Read())
        {
            Guid executionId = Guid.Parse(reader.GetString(0));
            KustoQueryResult? result = ReadResult(
                connection,
                executionId,
                reader.IsDBNull(12) ? null : reader.GetDouble(12),
                (KustoQueryResultCompleteness)reader.GetInt32(13));
            KustoRecordedRelationDescriptor? relation = reader.IsDBNull(14)
                ? null
                : new KustoRecordedRelationDescriptor(
                    reader.GetString(14),
                    reader.GetInt32(15) != 0,
                    ReadRelationColumns(connection, executionId));
            executions.Add(new KustoRecordedExecution(
                executionId,
                sessionId,
                Guid.Parse(reader.GetString(1)),
                reader.GetInt64(2),
                Guid.Parse(reader.GetString(3)),
                reader.GetString(4),
                new Uri(reader.GetString(5), UriKind.Absolute),
                reader.GetString(6),
                reader.GetString(7),
                ParseTimestamp(reader.GetString(8)),
                reader.IsDBNull(9) ? null : ParseTimestamp(reader.GetString(9)),
                (KustoRecordedExecutionStatus)reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                result,
                relation,
                reader.IsDBNull(16) ? null : reader.GetString(16)));
        }

        return executions.AsReadOnly();
    }

    private static ReadOnlyCollection<KustoSourceColumnLineage> ReadRelationColumns(
        SqliteConnection connection,
        Guid executionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT result_column_name, source_column_name
            FROM recorded_relation_columns WHERE execution_id = $executionId ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$executionId", FormatGuid(executionId));
        using SqliteDataReader reader = command.ExecuteReader();
        List<KustoSourceColumnLineage> columns = [];
        while (reader.Read())
        {
            columns.Add(new KustoSourceColumnLineage(reader.GetString(0), reader.GetString(1)));
        }

        return columns.AsReadOnly();
    }

    private static KustoQueryResult? ReadResult(
        SqliteConnection connection,
        Guid executionId,
        double? durationMilliseconds,
        KustoQueryResultCompleteness completeness)
    {
        if (durationMilliseconds is null)
        {
            return null;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, name FROM recorded_result_tables WHERE execution_id = $executionId ORDER BY ordinal;";
        command.Parameters.AddWithValue("$executionId", FormatGuid(executionId));
        using SqliteDataReader reader = command.ExecuteReader();
        List<KustoResultTable> tables = [];
        while (reader.Read())
        {
            Guid tableId = Guid.Parse(reader.GetString(0));
            ReadOnlyCollection<KustoResultColumn> columns = ReadColumns(connection, tableId);
            tables.Add(new KustoResultTable(
                reader.GetString(1),
                columns,
                ReadRows(connection, tableId, columns.Count)));
        }

        return new KustoQueryResult(
            tables,
            TimeSpan.FromMilliseconds(durationMilliseconds.Value),
            completeness: completeness);
    }

    private static ReadOnlyCollection<KustoResultColumn> ReadColumns(SqliteConnection connection, Guid tableId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name, type_name FROM recorded_result_columns WHERE table_id = $tableId ORDER BY ordinal;";
        command.Parameters.AddWithValue("$tableId", FormatGuid(tableId));
        using SqliteDataReader reader = command.ExecuteReader();
        List<KustoResultColumn> columns = [];
        while (reader.Read())
        {
            columns.Add(new KustoResultColumn(reader.GetString(0), reader.GetString(1)));
        }

        return columns.AsReadOnly();
    }

    private static ReadOnlyCollection<KustoResultRow> ReadRows(
        SqliteConnection connection,
        Guid tableId,
        int columnCount)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT display_values_json, raw_values_json
            FROM recorded_result_rows WHERE table_id = $tableId ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$tableId", FormatGuid(tableId));
        using SqliteDataReader reader = command.ExecuteReader();
        List<KustoResultRow> rows = [];
        while (reader.Read())
        {
            string[] displayValues = DeserializeStrings(reader.GetString(0));
            string?[] rawValues = DeserializeNullableStrings(reader.GetString(1));
            if (displayValues.Length != columnCount || rawValues.Length != columnCount)
            {
                throw new InvalidDataException("A recorded result row does not match its column count.");
            }

            KustoResultValue[] values = displayValues
                .Select((display, index) => new KustoResultValue(
                    display,
                    rawValues[index],
                    string.Equals(rawValues[index], "null", StringComparison.Ordinal)))
                .ToArray();
            rows.Add(new KustoResultRow(values));
        }

        return rows.AsReadOnly();
    }

    private static string[] DeserializeStrings(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
    }

    private static string?[] DeserializeNullableStrings(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement
            .EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.Null ? null : value.GetString())
            .ToArray();
    }

    private static ReadOnlyCollection<KustoRecordedInterest> ReadInterests(SqliteConnection connection, Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, declared_execution_id, source, column_name, type_name,
                   canonical_value, coordinate_execution_id, table_ordinal,
                   row_ordinal, column_ordinal, literal_start, literal_length,
                   is_suppressed
            FROM recorded_value_interests
            WHERE session_id = $sessionId
            ORDER BY rowid;
            """;
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        using SqliteDataReader reader = command.ExecuteReader();
        List<KustoRecordedInterest> interests = [];
        while (reader.Read())
        {
            KustoRecordedValueCoordinate? coordinate = reader.IsDBNull(6)
                ? null
                : new KustoRecordedValueCoordinate(
                    Guid.Parse(reader.GetString(6)),
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9));
            interests.Add(new KustoRecordedInterest(
                Guid.Parse(reader.GetString(0)),
                sessionId,
                Guid.Parse(reader.GetString(1)),
                (KustoRecordedInterestSource)reader.GetInt32(2),
                reader.GetString(3),
                new KustoRecordedValueIdentity(reader.GetString(4), reader.GetString(5), false),
                coordinate,
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.GetInt32(12) != 0));
        }

        return interests.AsReadOnly();
    }

    private static ReadOnlyCollection<KustoRecordedMark> ReadMarks(SqliteConnection connection, Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, execution_id, table_ordinal, row_ordinal, column_ordinal, created_at_utc
            FROM recorded_marks WHERE session_id = $sessionId ORDER BY created_at_utc;
            """;
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        using SqliteDataReader reader = command.ExecuteReader();
        List<KustoRecordedMark> marks = [];
        while (reader.Read())
        {
            marks.Add(new KustoRecordedMark(
                Guid.Parse(reader.GetString(0)),
                sessionId,
                (KustoRecordedMarkKind)reader.GetInt32(1),
                new KustoRecordedValueCoordinate(
                    Guid.Parse(reader.GetString(2)),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5)),
                ParseTimestamp(reader.GetString(6))));
        }

        return marks.AsReadOnly();
    }

    private static ReadOnlyCollection<KustoChainEndpoint> ReadEndpoints(SqliteConnection connection, Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT role, execution_id, table_ordinal, row_ordinal, column_ordinal
            FROM recorded_chain_endpoints WHERE session_id = $sessionId ORDER BY role;
            """;
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        using SqliteDataReader reader = command.ExecuteReader();
        List<KustoChainEndpoint> endpoints = [];
        while (reader.Read())
        {
            endpoints.Add(new KustoChainEndpoint(
                sessionId,
                (KustoChainEndpointRole)reader.GetInt32(0),
                new KustoRecordedValueCoordinate(
                    Guid.Parse(reader.GetString(1)),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4))));
        }

        return endpoints.AsReadOnly();
    }

    private void EnsureSessionCapacity(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM recorded_sessions;";
        long count = (long)(command.ExecuteScalar() ?? 0L);
        if (count >= maximumSessionCount)
        {
            throw new InvalidOperationException(
                $"Recorded session storage contains {count:N0} sessions. Delete a session before creating another.");
        }
    }

    private void EnsureExecutionCapacity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM recorded_executions WHERE session_id = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", FormatGuid(sessionId));
        long count = (long)(command.ExecuteScalar() ?? 0L);
        if (count >= maximumExecutionsPerSession)
        {
            throw new InvalidOperationException(
                $"This session contains {count:N0} recorded queries. Delete a query or start another session.");
        }
    }

    private void EnsureDatabaseCapacity(SqliteConnection connection, SqliteTransaction transaction)
    {
        long usedBytes = ReadDatabaseUsedBytes(connection, transaction);
        if (usedBytes >= maximumDatabaseBytes)
        {
            throw new InvalidOperationException(
                "Recorded session storage is full. Delete recorded queries or sessions before recording more data.");
        }
    }

    private int GetAvailableResultBytes(SqliteConnection connection, SqliteTransaction transaction)
    {
        long usedBytes = ReadDatabaseUsedBytes(connection, transaction);
        long availableBytes = maximumDatabaseBytes - usedBytes - CompletionMetadataReserveBytes;
        return (int)Math.Min(maximumResultBytesPerExecution, Math.Max(0, availableBytes));
    }

    private async Task ExecuteWriteAsync(
        Func<SqliteConnection, SqliteTransaction, Task> action,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(
                async () =>
                {
                    using SqliteConnection connection = OpenConnection();
                    await using SqliteTransaction transaction = (SqliteTransaction)await connection
                        .BeginTransactionAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await action(connection, transaction).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        Lazy<Task> currentInitialization = Volatile.Read(ref initializationTask);
        await currentInitialization.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }

    private Task InitializeAsync()
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(databaseDirectory);
            using SqliteConnection connection = OpenConnection();
            using (SqliteCommand journalCommand = connection.CreateCommand())
            {
                journalCommand.CommandText = "PRAGMA journal_mode = WAL;";
                journalCommand.ExecuteScalar();
            }

            KustoRecordedSessionSqliteSchema.EnsureCreated(connection);
            RecoverInterruptedRecordings(connection, DateTimeOffset.UtcNow);
        });
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }
}
