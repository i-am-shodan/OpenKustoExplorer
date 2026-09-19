using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Portable.Sessions;

namespace OpenKustoExplorer.Infrastructure.Tests.Sessions;

/// <summary>
/// Verifies the portable recorded-session store independently of a host storage API.
/// </summary>
public sealed class JsonKustoRecordedSessionStoreTests
{
    /// <summary>
    /// Verifies typed results, inferred interests, annotations, and endpoints survive snapshot reload.
    /// </summary>
    /// <returns>A task that completes after the reloaded aggregate is inspected.</returns>
    [Fact]
    public async Task SessionRoundTripPreservesInvestigationData()
    {
        MemorySnapshotStore snapshotStore = new();
        DateTimeOffset startedAtUtc = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
        Guid documentId = Guid.NewGuid();
        Guid sessionId;
        Guid executionId;

        using (JsonKustoRecordedSessionStore store = await JsonKustoRecordedSessionStore.CreateAsync(snapshotStore))
        {
            KustoRecordingPeriod period = await store.CreateSessionAsync("Browser investigation", startedAtUtc);
            sessionId = period.SessionId;
            executionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
                period.Id,
                documentId,
                "Outbound browsing",
                new KustoQueryRequest(
                    new Uri("https://mock.kusto.example/"),
                    "SyntheticSecurity",
                    "OutboundBrowsing | where url == 'https://malware.example.test'"),
                startedAtUtc,
                [new KustoPredicateInterest("url", "string", "https://malware.example.test", 34, 30)],
                new KustoRecordedRelationDescriptor(
                    "OutboundBrowsing",
                    true,
                    [new KustoSourceColumnLineage("Destination", "url")])));
            KustoResultTable table = new(
                "PrimaryResult",
                [
                    new KustoResultColumn("Destination", "string"),
                    new KustoResultColumn("SourceIp", "string"),
                ],
                [
                    new KustoResultRow(
                    [
                        new KustoResultValue(
                            "https://malware.example.test",
                            "\"https://malware.example.test\"",
                            false),
                        new KustoResultValue("192.0.2.42", "\"192.0.2.42\"", false),
                    ]),
                ]);
            await store.CompleteExecutionAsync(
                executionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    startedAtUtc.AddSeconds(1),
                    new KustoQueryResult([table], TimeSpan.FromSeconds(1)),
                    null));
            KustoRecordedValueCoordinate sourceIp = new(executionId, 0, 0, 1);
            KustoRecordedMark mark = await store.AddMarkAsync(
                sessionId,
                KustoRecordedMarkKind.Cell,
                sourceIp,
                startedAtUtc.AddSeconds(2));
            KustoRecordedMark duplicate = await store.AddMarkAsync(
                sessionId,
                KustoRecordedMarkKind.Cell,
                sourceIp,
                startedAtUtc.AddSeconds(3));
            await store.SetEndpointAsync(sessionId, KustoChainEndpointRole.Start, sourceIp);
            await store.StopRecordingAsync(period.Id, startedAtUtc.AddMinutes(1));

            Assert.Equal(mark.Id, duplicate.Id);
        }

        Assert.False(string.IsNullOrEmpty(snapshotStore.Json));
        using JsonKustoRecordedSessionStore reloaded = await JsonKustoRecordedSessionStore.CreateAsync(snapshotStore);
        KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
            await reloaded.GetSessionAsync(sessionId));

        Assert.Equal("Browser investigation", session.Summary.Name);
        Assert.True(session.Summary.StoredBytes > 0);
        KustoRecordedExecution execution = Assert.Single(session.Executions);
        Assert.Equal(executionId, execution.Id);
        Assert.Equal("https://malware.example.test", execution.Result!.Tables[0].Rows[0].Values[0]);
        Assert.Contains(
            session.Interests,
            interest => interest.Source == KustoRecordedInterestSource.ConfirmedPredicate);
        Assert.Contains(
            session.Interests,
            interest => interest.Source == KustoRecordedInterestSource.ManualCell);
        Assert.Equal(2, session.Marks.Count);
        Assert.Single(session.Endpoints);
    }

    /// <summary>
    /// Verifies reload closes open periods and marks running executions as interrupted.
    /// </summary>
    /// <returns>A task that completes after recovery is inspected.</returns>
    [Fact]
    public async Task ReloadRecoversInterruptedRecording()
    {
        MemorySnapshotStore snapshotStore = new();
        DateTimeOffset startedAtUtc = new(2026, 9, 14, 11, 0, 0, TimeSpan.Zero);
        Guid sessionId;

        using (JsonKustoRecordedSessionStore store = await JsonKustoRecordedSessionStore.CreateAsync(snapshotStore))
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
                    "Events | take 1"),
                startedAtUtc,
                [],
                null));
        }

        DateTimeOffset recoveredAtUtc = startedAtUtc.AddMinutes(5);
        using JsonKustoRecordedSessionStore reloaded = await JsonKustoRecordedSessionStore.CreateAsync(
            snapshotStore,
            new FixedTimeProvider(recoveredAtUtc));
        KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
            await reloaded.GetSessionAsync(sessionId));

        Assert.Equal(recoveredAtUtc, Assert.Single(session.Periods).StoppedAtUtc);
        KustoRecordedExecution execution = Assert.Single(session.Executions);
        Assert.Equal(KustoRecordedExecutionStatus.Interrupted, execution.Status);
        Assert.Equal(recoveredAtUtc, execution.CompletedAtUtc);
    }

    /// <summary>
    /// Verifies pausing closes the current period and atomically removes in-flight execution evidence.
    /// </summary>
    /// <returns>A task that completes after the session is resumed.</returns>
    [Fact]
    public async Task PauseRecordingDiscardsExecutionsAndAllowsResume()
    {
        MemorySnapshotStore snapshotStore = new();
        DateTimeOffset startedAtUtc = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        using JsonKustoRecordedSessionStore store = await JsonKustoRecordedSessionStore.CreateAsync(snapshotStore);
        KustoRecordingPeriod period = await store.CreateSessionAsync("Paused", startedAtUtc);
        Guid executionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
            period.Id,
            Guid.NewGuid(),
            "Query",
            new KustoQueryRequest(
                new Uri("https://mock.kusto.example/"),
                "SyntheticSecurity",
                "Events | where SourceIp == '192.0.2.44'"),
            startedAtUtc.AddSeconds(1),
            [new KustoPredicateInterest("SourceIp", "string", "192.0.2.44", 28, 12)],
            null));

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

    /// <summary>
    /// Verifies portable archives round-trip through the Browser JSON store with fresh linked identifiers.
    /// </summary>
    /// <returns>A task that completes after the imported copy is inspected.</returns>
    [Fact]
    public async Task SessionArchiveRoundTripRemapsAndPersistsEvidence()
    {
        MemorySnapshotStore snapshotStore = new();
        DateTimeOffset startedAtUtc = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        using JsonKustoRecordedSessionStore store = await JsonKustoRecordedSessionStore.CreateAsync(snapshotStore);
        KustoRecordingPeriod period = await store.CreateSessionAsync("Browser archive", startedAtUtc);
        Guid executionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
            period.Id,
            Guid.NewGuid(),
            "Archive query",
            new KustoQueryRequest(
                new Uri("https://mock.kusto.example/"),
                "SyntheticSecurity",
                "Events | take 1"),
            startedAtUtc.AddSeconds(1),
            [],
            null));
        KustoQueryResult result = new(
            [
                new KustoResultTable(
                    "PrimaryResult",
                    [new KustoResultColumn("SourceIp", "string")],
                    [new KustoResultRow([new KustoResultValue("192.0.2.42", "\"192.0.2.42\"", false)])]),
            ],
            TimeSpan.FromSeconds(1));
        await store.CompleteExecutionAsync(
            executionId,
            new KustoRecordedExecutionCompletion(
                KustoRecordedExecutionStatus.Succeeded,
                startedAtUtc.AddSeconds(2),
                result,
                null));
        KustoRecordedValueCoordinate coordinate = new(executionId, 0, 0, 0);
        KustoRecordedMark mark = await store.AddMarkAsync(
            period.SessionId,
            KustoRecordedMarkKind.Cell,
            coordinate,
            startedAtUtc.AddSeconds(3));
        await store.SetEndpointAsync(period.SessionId, KustoChainEndpointRole.Start, coordinate);
        await store.StopRecordingAsync(period.Id, startedAtUtc.AddMinutes(1));

        KustoRecordedSessionArchiveService archiveService = new(store, TimeProvider.System);
        using MemoryStream archive = new();
        await archiveService.ExportAsync(period.SessionId, archive);
        archive.Position = 0;
        KustoRecordedSessionSummary importedSummary = await archiveService.ImportCopyAsync(archive);
        KustoRecordedSession imported = Assert.IsType<KustoRecordedSession>(
            await store.GetSessionAsync(importedSummary.Id));

        Assert.NotEqual(period.SessionId, importedSummary.Id);
        Assert.Equal("Browser archive (imported)", importedSummary.Name);
        KustoRecordedExecution importedExecution = Assert.Single(imported.Executions);
        Assert.NotEqual(executionId, importedExecution.Id);
        KustoRecordedMark importedMark = Assert.Single(imported.Marks);
        Assert.NotEqual(mark.Id, importedMark.Id);
        Assert.Equal(importedExecution.Id, importedMark.Coordinate.ExecutionId);
        Assert.Equal(importedExecution.Id, Assert.Single(imported.Endpoints).Coordinate.ExecutionId);
        Assert.Equal(importedMark.Id, Assert.Single(imported.Interests).MarkId);
        Assert.Equal(2, (await store.GetSessionsAsync()).Count);
        Assert.False(string.IsNullOrWhiteSpace(snapshotStore.Json));
    }

    /// <summary>
    /// Verifies invalid durable JSON is offered to the host recovery callback before an empty session store is used.
    /// </summary>
    /// <returns>A task that completes after the recovered store is inspected.</returns>
    [Fact]
    public async Task InvalidSnapshotInvokesRecoveryCallback()
    {
        const string InvalidJson = "{\"version\":999}";
        MemorySnapshotStore snapshotStore = new() { Json = InvalidJson };
        string? recoveredJson = null;

        using JsonKustoRecordedSessionStore store = await JsonKustoRecordedSessionStore.CreateAsync(
            snapshotStore,
            invalidSnapshotHandler: (json, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                recoveredJson = json;
                return Task.CompletedTask;
            });

        Assert.Equal(InvalidJson, recoveredJson);
        Assert.Empty(await store.GetSessionsAsync());
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            this.utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MemorySnapshotStore : IKustoRecordedSessionSnapshotStore
    {
        internal string? Json { get; set; }

        public Task<string?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Json);
        }

        public Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Json = snapshotJson;
            return Task.CompletedTask;
        }
    }
}
