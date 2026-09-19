using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using OpenKustoExplorer.Portable.Assistance;
using OpenKustoExplorer.Portable.Graphs;
using OpenKustoExplorer.Portable.Sessions;

namespace OpenKustoExplorer.Infrastructure.Tests.Assistance;

/// <summary>
/// Verifies consent and byte bounds for browser-local Copilot snapshots.
/// </summary>
public sealed class KustoCopilotSharedDataBuilderTests
{
    /// <summary>
    /// Verifies disabled graph and session consent never accesses local stores.
    /// </summary>
    /// <returns>A task that completes after existing consented data is preserved.</returns>
    [Fact]
    public async Task DisabledConsentDoesNotReadLocalStores()
    {
        KustoCopilotSharedDataBuilder builder = new(
            CreateThrowingProxy<IGraphStore>(),
            CreateThrowingProxy<IGraphQueryService>(),
            CreateThrowingProxy<IKustoRecordedSessionStore>());
        KustoCopilotContext context = new(
            Guid.NewGuid(),
            KustoCopilotScopeKind.Graph,
            "Investigation",
            "MATCH (n) RETURN n",
            "hidden",
            string.Empty,
            "Already consented query rows",
            new GraphSnapshot(Guid.NewGuid(), Guid.NewGuid()));

        string sharedData = await builder.CreateAsync(
            context,
            new KustoCopilotOptions("model", false, false, false, false));

        Assert.Contains("Already consented query rows", sharedData, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies multibyte shared data is truncated on a valid character boundary within 64 KiB.
    /// </summary>
    /// <returns>A task that completes after the payload is measured.</returns>
    [Fact]
    public async Task SharedDataUsesStrictUtf8ByteLimit()
    {
        KustoCopilotSharedDataBuilder builder = new(
            CreateThrowingProxy<IGraphStore>(),
            CreateThrowingProxy<IGraphQueryService>(),
            CreateThrowingProxy<IKustoRecordedSessionStore>());
        KustoCopilotContext context = new(
            Guid.NewGuid(),
            "Query",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Concat(Enumerable.Repeat("😀", 30_000)));

        string sharedData = await builder.CreateAsync(
            context,
            new KustoCopilotOptions("model", false, false));

        Assert.True(Encoding.UTF8.GetByteCount(sharedData) <= KustoCopilotSharedDataBuilder.MaximumSnapshotBytes);
        Assert.EndsWith("(shared data truncated)", sharedData, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies graph consent includes bounded entities, properties, schema, and relationships.
    /// </summary>
    /// <returns>A task that completes after the graph snapshot is created.</returns>
    [Fact]
    public async Task GraphConsentCreatesBoundedLocalSnapshot()
    {
        using JsonGraphStore graphStore = await JsonGraphStore.CreateAsync(new MemoryGraphSnapshotStore());
        DateTimeOffset discoveredAt = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        GraphEntityKey user = new(GraphEntityKind.User, "User", "alice@example.com");
        GraphEntityKey host = new(GraphEntityKind.Host, "Host", "server-1");
        await graphStore.ImportAsync(
            CreateGraphBatch(discoveredAt, user, host),
            GraphImportMode.Add);
        GraphStateSummary state = await graphStore.GetStateAsync();
        KustoCopilotSharedDataBuilder builder = new(
            graphStore,
            graphStore,
            CreateThrowingProxy<IKustoRecordedSessionStore>());
        KustoCopilotContext context = new(
            state.GenerationId,
            KustoCopilotScopeKind.Graph,
            state.GraphName,
            "MATCH (n) RETURN n",
            "2 nodes",
            string.Empty,
            string.Empty,
            state.Snapshot);

        string sharedData = await builder.CreateAsync(
            context,
            new KustoCopilotOptions("model", false, false, true));

        Assert.Contains("Consented graph snapshot", sharedData, StringComparison.Ordinal);
        Assert.Contains("alice@example.com", sharedData, StringComparison.Ordinal);
        Assert.Contains("enabled=true", sharedData, StringComparison.Ordinal);
        Assert.Contains("AuthenticatedTo", sharedData, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(sharedData) <= KustoCopilotSharedDataBuilder.MaximumSnapshotBytes);
    }

    /// <summary>
    /// Verifies recorded-session consent includes bounded retained result rows.
    /// </summary>
    /// <returns>A task that completes after the session snapshot is created.</returns>
    [Fact]
    public async Task RecordedSessionConsentCreatesBoundedLocalSnapshot()
    {
        using JsonKustoRecordedSessionStore sessionStore = await JsonKustoRecordedSessionStore.CreateAsync(
            new MemoryRecordedSessionSnapshotStore());
        DateTimeOffset startedAtUtc = new(2026, 9, 14, 13, 0, 0, TimeSpan.Zero);
        KustoRecordingPeriod period = await sessionStore.CreateSessionAsync("Browser investigation", startedAtUtc);
        Guid executionId = await sessionStore.BeginExecutionAsync(new KustoRecordedExecutionStart(
            period.Id,
            Guid.NewGuid(),
            "Outbound browsing",
            new KustoQueryRequest(
                new Uri("https://cluster.example.com"),
                "Security",
                "Events | take 1"),
            startedAtUtc,
            [],
            null));
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Account", "string")],
            [new KustoResultRow([new KustoResultValue("alice@example.com", null, false)])]);
        await sessionStore.CompleteExecutionAsync(
            executionId,
            new KustoRecordedExecutionCompletion(
                KustoRecordedExecutionStatus.Succeeded,
                startedAtUtc.AddSeconds(1),
                new KustoQueryResult([table], TimeSpan.FromSeconds(1)),
                null));
        KustoCopilotSharedDataBuilder builder = new(
            CreateThrowingProxy<IGraphStore>(),
            CreateThrowingProxy<IGraphQueryService>(),
            sessionStore);
        KustoCopilotContext context = new(
            period.SessionId,
            KustoCopilotScopeKind.RecordedSession,
            "Browser investigation",
            string.Empty,
            "hidden",
            string.Empty,
            string.Empty,
            null,
            new KustoCopilotRecordedSessionScope(period.SessionId, null));

        string sharedData = await builder.CreateAsync(
            context,
            new KustoCopilotOptions("model", false, false, false, true));

        Assert.Contains("Consented recorded-session result snapshot", sharedData, StringComparison.Ordinal);
        Assert.Contains("PrimaryResult", sharedData, StringComparison.Ordinal);
        Assert.Contains("alice@example.com", sharedData, StringComparison.Ordinal);
        Assert.DoesNotContain("cluster.example.com", sharedData, StringComparison.Ordinal);
    }

    private static T CreateThrowingProxy<T>()
        where T : class
    {
        return DispatchProxy.Create<T, ThrowingProxy>();
    }

    private static GraphImportBatch CreateGraphBatch(
        DateTimeOffset discoveredAt,
        GraphEntityKey user,
        GraphEntityKey host)
    {
        GraphEvidence evidence = new(
            "occurrence-1",
            "hash-1",
            "PrimaryResult",
            0,
            "{}",
            "{}");
        GraphTemporalInterval interval = new(discoveredAt);
        return new GraphImportBatch(
            new GraphIngestion(
                Guid.NewGuid(),
                GraphIngestionSourceKind.ManualQuery,
                Guid.NewGuid(),
                "Graph query",
                new Uri("https://cluster.example.com"),
                "Security",
                "Events | make-graph source --> target",
                discoveredAt,
                discoveredAt),
            [evidence],
            [
                new GraphEntityObservation(
                    Guid.NewGuid(),
                    user,
                    "Alice",
                    ["AZUser"],
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["enabled"] = "true" },
                    interval,
                    [evidence.OccurrenceId]),
                new GraphEntityObservation(
                    Guid.NewGuid(),
                    host,
                    "Server 1",
                    ["Device"],
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    interval,
                    [evidence.OccurrenceId]),
            ],
            [
                new GraphRelationshipObservation(
                    Guid.NewGuid(),
                    new GraphRelationshipKey(user, host, "AuthenticatedTo"),
                    ["SIGN_IN"],
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    interval,
                    [evidence.OccurrenceId]),
            ]);
    }

    private sealed class MemoryGraphSnapshotStore : IGraphSnapshotStore
    {
        private string? json;

        public Task<string?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(json);
        }

        public Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            json = snapshotJson;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryRecordedSessionSnapshotStore : IKustoRecordedSessionSnapshotStore
    {
        private string? json;

        public Task<string?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(json);
        }

        public Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            json = snapshotJson;
            return Task.CompletedTask;
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1852:Seal internal types",
        Justification = "DispatchProxy creates a runtime subclass of this proxy type.")]
    private class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _ = targetMethod;
            _ = args;
            throw new InvalidOperationException("A local store was accessed without consent.");
        }
    }
}
