using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Infrastructure.Sessions;

namespace OpenKustoExplorer.Infrastructure.Tests.Sessions;

/// <summary>
/// Verifies transactional persistence for recorded query sessions.
/// </summary>
public sealed class SqliteKustoRecordedSessionStoreTests
{
    /// <summary>
    /// Verifies constructing the store does not initialize its database before the first operation.
    /// </summary>
    /// <returns>A task that completes after deferred initialization.</returns>
    [Fact]
    public async Task ConstructorDefersDatabaseInitializationUntilFirstOperation()
    {
        string directoryPath = CreateTemporaryDirectory();
        string databaseDirectory = Path.Combine(directoryPath, "sessions");
        string filePath = Path.Combine(databaseDirectory, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);

            Assert.False(Directory.Exists(databaseDirectory));

            Assert.Empty(await store.GetSessionsAsync());
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies a new store creates the initial recorded-session schema version.
    /// </summary>
    /// <returns>A task that completes after the schema version is read.</returns>
    [Fact]
    public async Task FirstOperationCreatesSchemaVersionOne()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using (SqliteKustoRecordedSessionStore store = new(filePath))
            {
                Assert.Empty(await store.GetSessionsAsync());
            }

            using SqliteConnection connection = new($"Data Source={filePath}");
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";

            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies an incompatible pre-release database can be explicitly replaced without recreating the store.
    /// </summary>
    /// <returns>A task that completes after empty storage is recreated.</returns>
    [Fact]
    public async Task ResetDatabaseReplacesIncompatiblePreReleaseSchema()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using (SqliteKustoRecordedSessionStore initialStore = new(filePath))
            {
                Assert.Empty(await initialStore.GetSessionsAsync());
            }

            using (SqliteConnection connection = new($"Data Source={filePath}"))
            {
                await connection.OpenAsync();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version = 4;";
                await command.ExecuteNonQueryAsync();
            }

            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordedSessionDatabaseVersionException exception =
                await Assert.ThrowsAsync<KustoRecordedSessionDatabaseVersionException>(
                    () => store.GetSessionsAsync());

            Assert.Equal(4, exception.DatabaseVersion);
            Assert.Equal(1, exception.SupportedVersion);
            Assert.Equal(directoryPath, store.DatabaseDirectoryPath);

            await store.ResetDatabaseAsync();

            Assert.Empty(await store.GetSessionsAsync());
            using SqliteConnection resetConnection = new($"Data Source={filePath}");
            await resetConnection.OpenAsync();
            using SqliteCommand versionCommand = resetConnection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            Assert.Equal(1L, await versionCommand.ExecuteScalarAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies the store requires explicit deletion before creating more than its session budget.
    /// </summary>
    /// <returns>A task that completes after the boundary is rejected.</returns>
    [Fact]
    public async Task CreateSessionRejectsConfiguredSessionLimit()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(
                filePath,
                maximumSessionCount: 1,
                maximumExecutionsPerSession: 10,
                maximumDatabaseBytes: SqliteKustoRecordedSessionStore.MaximumDatabaseBytes,
                maximumResultBytesPerExecution: 1024);
            await store.CreateSessionAsync("First", DateTimeOffset.UtcNow);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.CreateSessionAsync("Second", DateTimeOffset.UtcNow));

            Assert.Contains("Delete a session", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies one session cannot accumulate more executions than its configured budget.
    /// </summary>
    /// <returns>A task that completes after the boundary is rejected.</returns>
    [Fact]
    public async Task BeginExecutionRejectsConfiguredSessionExecutionLimit()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            using SqliteKustoRecordedSessionStore store = new(
                filePath,
                maximumSessionCount: 10,
                maximumExecutionsPerSession: 1,
                maximumDatabaseBytes: SqliteKustoRecordedSessionStore.MaximumDatabaseBytes,
                maximumResultBytesPerExecution: 1024);
            KustoRecordingPeriod period = await store.CreateSessionAsync("Bounded", startedAtUtc);
            await store.BeginExecutionAsync(CreateExecutionStart(period, "First", "print 1", startedAtUtc));

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.BeginExecutionAsync(CreateExecutionStart(period, "Second", "print 2", startedAtUtc)));

            Assert.Contains("Delete a query", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies logical database capacity blocks new sessions without deleting retained data.
    /// </summary>
    /// <returns>A task that completes after the full-store boundary is rejected.</returns>
    [Fact]
    public async Task CreateSessionRejectsConfiguredDatabaseCapacity()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(
                filePath,
                maximumSessionCount: 10,
                maximumExecutionsPerSession: 10,
                maximumDatabaseBytes: 1,
                maximumResultBytesPerExecution: 1024);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.CreateSessionAsync("Full", DateTimeOffset.UtcNow));

            Assert.Contains("storage is full", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await store.GetSessionsAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies oversized query text is rejected before it can consume persistent storage.
    /// </summary>
    /// <returns>A task that completes after the query boundary is rejected.</returns>
    [Fact]
    public async Task BeginExecutionRejectsOversizedQueryText()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingPeriod period = await store.CreateSessionAsync("Query limit", startedAtUtc);
            string queryText = new('x', SqliteKustoRecordedSessionStore.MaximumRecordedQueryBytes + 1);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.BeginExecutionAsync(CreateExecutionStart(period, "Large", queryText, startedAtUtc)));

            Assert.Contains("query text exceeds", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies result persistence truncates whole rows when its configured byte budget is reached.
    /// </summary>
    /// <returns>A task that completes after the bounded result is reloaded.</returns>
    [Fact]
    public async Task CompleteExecutionLimitsStoredResultBytes()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            using SqliteKustoRecordedSessionStore store = new(
                filePath,
                maximumSessionCount: 10,
                maximumExecutionsPerSession: 10,
                maximumDatabaseBytes: SqliteKustoRecordedSessionStore.MaximumDatabaseBytes,
                maximumResultBytesPerExecution: 1024);
            KustoRecordingPeriod period = await store.CreateSessionAsync("Byte limit", startedAtUtc);
            Guid executionId = await store.BeginExecutionAsync(
                CreateExecutionStart(period, "Payload", "print value", startedAtUtc));
            KustoResultTable table = new(
                "Primary",
                [new KustoResultColumn("Value", "string")],
                [new KustoResultRow(["kept"]), new KustoResultRow([new string('x', 5000)])]);

            await store.CompleteExecutionAsync(
                executionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    startedAtUtc.AddSeconds(1),
                    new KustoQueryResult([table], TimeSpan.FromSeconds(1)),
                    null));

            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(period.SessionId));
            KustoQueryResult result = Assert.IsType<KustoQueryResult>(Assert.Single(session.Executions).Result);
            Assert.Equal(KustoQueryResultCompleteness.RecordLimitReached, result.Completeness);
            Assert.Equal("kept", Assert.Single(Assert.Single(result.Tables).Rows).Values[0]);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies recorded executions retain no more than 500 rows across all result tables.
    /// </summary>
    /// <returns>A task that completes after the bounded result is reloaded.</returns>
    [Fact]
    public async Task CompleteExecutionLimitsStoredResultsToFiveHundredRows()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");
        DateTimeOffset startedAtUtc = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingPeriod period = await store.CreateSessionAsync("Bounded results", startedAtUtc);
            Guid executionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
                period.Id,
                Guid.NewGuid(),
                "Large query",
                new KustoQueryRequest(
                    new Uri("https://mock.kusto.example/"),
                    "SyntheticData",
                    "StormEvents | take 600"),
                startedAtUtc,
                [],
                null));
            KustoResultColumn[] columns = [new KustoResultColumn("Value", "long")];
            KustoResultTable firstTable = new(
                "First",
                columns,
                Enumerable.Range(0, 400).Select(index => new KustoResultRow(
                    [index.ToString(CultureInfo.InvariantCulture)])));
            KustoResultTable secondTable = new(
                "Second",
                columns,
                Enumerable.Range(400, 200).Select(index => new KustoResultRow(
                    [index.ToString(CultureInfo.InvariantCulture)])));

            await store.CompleteExecutionAsync(
                executionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    startedAtUtc.AddSeconds(1),
                    new KustoQueryResult([firstTable, secondTable], TimeSpan.FromSeconds(1)),
                    null));

            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(period.SessionId));
            KustoQueryResult result = Assert.IsType<KustoQueryResult>(Assert.Single(session.Executions).Result);
            Assert.Equal(KustoQueryResultCompleteness.RecordLimitReached, result.Completeness);
            Assert.Equal(400, result.Tables[0].Rows.Count);
            Assert.Equal(100, result.Tables[1].Rows.Count);
            Assert.Equal(
                SqliteKustoRecordedSessionStore.MaximumRecordedRowsPerExecution,
                result.Tables.Sum(table => table.Rows.Count));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies exact predicate values returned by a successful query receive cell marks.
    /// </summary>
    /// <returns>A task that completes after both executions are persisted.</returns>
    [Fact]
    public async Task SuccessfulPredicateResultPromotesMatchingValueToCellMark()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");
        DateTimeOffset startedAtUtc = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingPeriod period = await store.CreateSessionAsync("Predicate promotion", startedAtUtc);
            Guid matchingExecutionId = await BeginPredicateExecutionAsync(
                store,
                period,
                "192.0.2.56",
                startedAtUtc);
            KustoResultTable matchingResult = new(
                "PrimaryResult",
                [
                    new KustoResultColumn("source_address", "string"),
                    new KustoResultColumn("username", "string"),
                ],
                [new KustoResultRow(["192.0.2.56", "synthetic-user"])]);
            await store.CompleteExecutionAsync(
                matchingExecutionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    startedAtUtc.AddSeconds(1),
                    new KustoQueryResult([matchingResult], TimeSpan.FromSeconds(1)),
                    null));

            Guid emptyExecutionId = await BeginPredicateExecutionAsync(
                store,
                period,
                "192.0.2.99",
                startedAtUtc.AddMinutes(1));
            KustoResultTable emptyResult = new(
                "PrimaryResult",
                matchingResult.Columns,
                []);
            await store.CompleteExecutionAsync(
                emptyExecutionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    startedAtUtc.AddMinutes(1).AddSeconds(1),
                    new KustoQueryResult([emptyResult], TimeSpan.FromSeconds(1)),
                    null));

            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(period.SessionId));
            KustoRecordedMark mark = Assert.Single(session.Marks);
            Assert.Equal(matchingExecutionId, mark.Coordinate.ExecutionId);
            Assert.Equal(0, mark.Coordinate.TableOrdinal);
            Assert.Equal(0, mark.Coordinate.RowOrdinal);
            Assert.Equal(0, mark.Coordinate.ColumnOrdinal);
            Assert.Contains(
                session.Interests,
                interest => interest.Source == KustoRecordedInterestSource.ConfirmedPredicate
                    && interest.DeclaredExecutionId == matchingExecutionId
                    && interest.Identity.CanonicalValue == "192.0.2.56");
            Assert.DoesNotContain(
                session.Interests,
                interest => interest.Source == KustoRecordedInterestSource.ConfirmedPredicate
                    && interest.DeclaredExecutionId == emptyExecutionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies complete session data, annotations, append periods, and deletion survive database reopen.
    /// </summary>
    /// <returns>A task that completes after the round trip.</returns>
    [Fact]
    public async Task SessionRoundTripPreservesTypedResultsAndAnnotations()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");
        DateTimeOffset startedAtUtc = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        Guid documentId = Guid.NewGuid();
        Guid sessionId;
        Guid executionId;

        try
        {
            using (SqliteKustoRecordedSessionStore store = new(filePath))
            {
                KustoRecordingPeriod period = await store.CreateSessionAsync("C2 investigation", startedAtUtc);
                sessionId = period.SessionId;
                KustoPredicateInterest predicateInterest = new(
                    "url",
                    "string",
                    "https://malware.example.test/payload",
                    31,
                    38);
                KustoRecordedRelationDescriptor relation = new(
                    "OutboundBrowsing",
                    true,
                    [
                        new KustoSourceColumnLineage("url", "url"),
                        new KustoSourceColumnLineage("src_ip", "src_ip"),
                        new KustoSourceColumnLineage("nullable", "nullable"),
                    ]);
                executionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
                    period.Id,
                    documentId,
                    "Outbound browsing",
                    new KustoQueryRequest(
                        new Uri("https://mock.kusto.example/"),
                        "SyntheticSecurity",
                        "OutboundBrowsing | where url == \"https://malware.example.test/payload\""),
                    startedAtUtc,
                    [predicateInterest],
                    relation));
                KustoResultTable resultTable = new(
                    "PrimaryResult",
                    [
                        new KustoResultColumn("url", "string"),
                        new KustoResultColumn("src_ip", "string"),
                        new KustoResultColumn("nullable", "string"),
                    ],
                    [
                        new KustoResultRow(
                        [
                            new KustoResultValue(
                                "https://malware.example.test/payload",
                                "\"https://malware.example.test/payload\"",
                                false),
                            new KustoResultValue("192.0.2.56", "\"192.0.2.56\"", false),
                            new KustoResultValue(string.Empty, "null", true),
                        ]),
                    ]);
                KustoResultTable secondaryTable = new(
                    "SecondaryResult",
                    [new KustoResultColumn("message", "string")],
                    [new KustoResultRow([new KustoResultValue("retained", "\"retained\"", false)])]);
                await store.CompleteExecutionAsync(
                    executionId,
                    new KustoRecordedExecutionCompletion(
                        KustoRecordedExecutionStatus.Succeeded,
                        startedAtUtc.AddSeconds(1),
                        new KustoQueryResult([resultTable, secondaryTable], TimeSpan.FromSeconds(1)),
                        null));
                await store.RenameExecutionAsync(executionId, "Suspicious outbound activity");
                KustoRecordedValueCoordinate ipCoordinate = new(executionId, 0, 0, 1);
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AddMarkAsync(
                    sessionId,
                    KustoRecordedMarkKind.Row,
                    ipCoordinate,
                    startedAtUtc.AddSeconds(2)));
                KustoRecordedMark firstMark = await store.AddMarkAsync(
                    sessionId,
                    KustoRecordedMarkKind.Cell,
                    ipCoordinate,
                    startedAtUtc.AddSeconds(2));
                KustoRecordedMark duplicateMark = await store.AddMarkAsync(
                    sessionId,
                    KustoRecordedMarkKind.Cell,
                    ipCoordinate,
                    startedAtUtc.AddSeconds(3));
                await store.SetEndpointAsync(
                    sessionId,
                    KustoChainEndpointRole.Start,
                    new KustoRecordedValueCoordinate(executionId, 0, 0, 0));
                await store.StopRecordingAsync(period.Id, startedAtUtc.AddMinutes(1));
                KustoRecordingPeriod appended = await store.AppendSessionAsync(
                    sessionId,
                    startedAtUtc.AddMinutes(2));
                await store.StopRecordingAsync(appended.Id, startedAtUtc.AddMinutes(3));

                Assert.Equal(firstMark.Id, duplicateMark.Id);
            }

            using (SqliteKustoRecordedSessionStore store = new(filePath))
            {
                KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                    await store.GetSessionAsync(sessionId));
                long storedBytesWithExecution = Assert.Single(await store.GetSessionsAsync()).StoredBytes;

                Assert.Equal("C2 investigation", session.Summary.Name);
                Assert.True(storedBytesWithExecution > 0);
                Assert.Equal(2, session.Periods.Count);
                KustoRecordedExecution execution = Assert.Single(session.Executions);
                Assert.Equal(executionId, execution.Id);
                Assert.Equal("Suspicious outbound activity", execution.DisplayName);
                Assert.Equal("OutboundBrowsing", execution.Relation?.SourceTableName);
                Assert.True(execution.Relation?.IsComposable);
                Assert.Equal(2, execution.Result!.Tables.Count);
                KustoResultRow row = Assert.Single(execution.Result.Tables[0].Rows);
                Assert.Equal("192.0.2.56", row.Values[1]);
                Assert.Equal("\"192.0.2.56\"", row.ResultValues[1].RawJson);
                Assert.True(row.ResultValues[2].IsNull);
                Assert.Equal("retained", Assert.Single(execution.Result.Tables[1].Rows).Values[0]);
                Assert.Equal(3, session.Interests.Count);
                Assert.Contains(session.Interests, interest => interest.Source == KustoRecordedInterestSource.QueryPredicate);
                Assert.Contains(
                    session.Interests,
                    interest => interest.Source == KustoRecordedInterestSource.ConfirmedPredicate);
                Assert.Contains(session.Interests, interest => interest.Source == KustoRecordedInterestSource.ManualCell);
                Assert.Equal(2, session.Marks.Count);
                KustoRecordedMark storedMark = Assert.Single(session.Marks, mark =>
                    mark.Coordinate.ExecutionId == executionId
                    && mark.Coordinate.ColumnOrdinal == 1);
                Assert.Single(session.Endpoints);

                await store.RemoveMarkAsync(storedMark.Id);
                await store.ClearEndpointAsync(sessionId, KustoChainEndpointRole.Start);
                KustoRecordedSession unmarked = Assert.IsType<KustoRecordedSession>(
                    await store.GetSessionAsync(sessionId));
                Assert.Single(unmarked.Marks);
                Assert.Empty(unmarked.Endpoints);
                Assert.DoesNotContain(
                    unmarked.Interests,
                    interest => interest.Source == KustoRecordedInterestSource.ManualCell);

                await store.DeleteExecutionAsync(executionId);
                KustoRecordedSession withoutExecution = Assert.IsType<KustoRecordedSession>(
                    await store.GetSessionAsync(sessionId));
                Assert.Empty(withoutExecution.Executions);
                Assert.Empty(withoutExecution.Interests);
                Assert.Empty(withoutExecution.Marks);
                Assert.Empty(withoutExecution.Endpoints);
                KustoRecordedSessionSummary summaryWithoutExecution = Assert.Single(await store.GetSessionsAsync());
                Assert.Equal(0, summaryWithoutExecution.ExecutionCount);
                Assert.True(summaryWithoutExecution.StoredBytes < storedBytesWithExecution);

                await store.DeleteSessionAsync(sessionId);
                Assert.Null(await store.GetSessionAsync(sessionId));
                Assert.Empty(await store.GetSessionsAsync());
            }
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies startup recovers open periods and running executions as interrupted.
    /// </summary>
    /// <returns>A task that completes after recovery.</returns>
    [Fact]
    public async Task ReopenRecoversInterruptedRecording()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");
        DateTimeOffset startedAtUtc = new(2026, 9, 6, 13, 0, 0, TimeSpan.Zero);
        Guid sessionId;

        try
        {
            using (SqliteKustoRecordedSessionStore store = new(filePath))
            {
                KustoRecordingPeriod period = await store.CreateSessionAsync("Interrupted", startedAtUtc);
                sessionId = period.SessionId;
                await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
                    period.Id,
                    Guid.NewGuid(),
                    "Query",
                    new KustoQueryRequest(
                        new Uri("https://mock.kusto.example/"),
                        "SyntheticSecurity",
                        "Employees | take 1"),
                    startedAtUtc,
                    [],
                    null));
            }

            using SqliteKustoRecordedSessionStore reopened = new(filePath);
            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await reopened.GetSessionAsync(sessionId));
            Assert.NotNull(Assert.Single(session.Periods).StoppedAtUtc);
            KustoRecordedExecution execution = Assert.Single(session.Executions);
            Assert.Equal(KustoRecordedExecutionStatus.Interrupted, execution.Status);
            Assert.Contains("interrupted", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies pausing closes the current period and atomically removes in-flight execution evidence.
    /// </summary>
    /// <returns>A task that completes after the session is resumed.</returns>
    [Fact]
    public async Task PauseRecordingDiscardsExecutionsAndAllowsResume()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Join(directoryPath, "recorded-sessions.db");
        DateTimeOffset startedAtUtc = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingPeriod period = await store.CreateSessionAsync("Paused", startedAtUtc);
            Guid executionId = await BeginPredicateExecutionAsync(
                store,
                period,
                "192.0.2.44",
                startedAtUtc.AddSeconds(1));

            await store.PauseRecordingAsync(
                period.Id,
                startedAtUtc.AddSeconds(2),
                [executionId]);

            KustoRecordedSession paused = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(period.SessionId));
            Assert.NotNull(Assert.Single(paused.Periods).StoppedAtUtc);
            Assert.Empty(paused.Executions);
            Assert.Empty(paused.Interests);

            KustoRecordingPeriod resumed = await store.AppendSessionAsync(
                period.SessionId,
                startedAtUtc.AddSeconds(3));
            await store.StopRecordingAsync(resumed.Id, startedAtUtc.AddSeconds(4));

            KustoRecordedSession completed = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(period.SessionId));
            Assert.Equal(2, completed.Periods.Count);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies dynamic objects canonicalize independently of property order.
    /// </summary>
    [Fact]
    public void CanonicalizerSortsDynamicPropertiesAndPreservesTypes()
    {
        KustoRecordedValueIdentity first = KustoRecordedValueCanonicalizer.Create(
            "dynamic",
            new KustoResultValue("{\"b\":2,\"a\":1}", "{\"b\":2,\"a\":1}", false));
        KustoRecordedValueIdentity second = KustoRecordedValueCanonicalizer.Create(
            "dynamic",
            new KustoResultValue("{\"a\":1,\"b\":2}", "{\"a\":1,\"b\":2}", false));
        KustoRecordedValueIdentity text = KustoRecordedValueCanonicalizer.Create(
            "string",
            new KustoResultValue("1", "\"1\"", false));
        KustoRecordedValueIdentity integer = KustoRecordedValueCanonicalizer.Create(
            "long",
            new KustoResultValue("1", "1", false));

        Assert.Equal(first, second);
        Assert.Equal(KustoRecordedValueCanonicalizer.CreateHash(first), KustoRecordedValueCanonicalizer.CreateHash(second));
        Assert.NotEqual(text, integer);
    }

    private static Task<Guid> BeginPredicateExecutionAsync(
        SqliteKustoRecordedSessionStore store,
        KustoRecordingPeriod period,
        string value,
        DateTimeOffset startedAtUtc)
    {
        KustoPredicateInterest interest = new("src_ip", "string", value, 44, value.Length + 2);
        KustoRecordedRelationDescriptor relation = new(
            "AuthenticationEvents",
            true,
            [
                new KustoSourceColumnLineage("source_address", "src_ip"),
                new KustoSourceColumnLineage("username", "username"),
            ]);
        return store.BeginExecutionAsync(new KustoRecordedExecutionStart(
            period.Id,
            Guid.NewGuid(),
            "Authentication",
            new KustoQueryRequest(
                new Uri("https://mock.kusto.example/"),
                "SyntheticSecurity",
                $"AuthenticationEvents | where src_ip == \"{value}\""),
            startedAtUtc,
            [interest],
            relation));
    }

    private static KustoRecordedExecutionStart CreateExecutionStart(
        KustoRecordingPeriod period,
        string title,
        string queryText,
        DateTimeOffset startedAtUtc)
    {
        return new KustoRecordedExecutionStart(
            period.Id,
            Guid.NewGuid(),
            title,
            new KustoQueryRequest(
                new Uri("https://mock.kusto.example/"),
                "SyntheticData",
                queryText),
            startedAtUtc,
            [],
            null);
    }

    private static string CreateTemporaryDirectory()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-RecordedSessions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static void DeleteTemporaryDirectory(string directoryPath)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, true);
        }
    }
}
