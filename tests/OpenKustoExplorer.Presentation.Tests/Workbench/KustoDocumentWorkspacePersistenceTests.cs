using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Workbench;

/// <summary>
/// Verifies document persistence failure containment.
/// </summary>
public sealed class KustoDocumentWorkspacePersistenceTests
{
    /// <summary>
    /// Verifies an unexpected store failure is exposed without escaping the persistence boundary.
    /// </summary>
    /// <returns>A task that completes after the durable save fails.</returns>
    [Fact]
    public async Task FlushReportsUnexpectedStoreFailure()
    {
        using KustoDocumentWorkspacePersistence persistence = new(new ThrowingDocumentStore());
        string? error = null;
        persistence.SaveErrorChanged += value => error = value;

        await persistence.FlushAsync(new KustoDocumentWorkspace([], null));

        Assert.Contains("Queries are not saved", error, StringComparison.Ordinal);
        Assert.Contains("Unexpected store failure", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies rapid changes defer snapshot construction and persist only the latest workspace.
    /// </summary>
    /// <returns>A task that completes after the debounced save.</returns>
    [Fact]
    public async Task ScheduleDefersAndCoalescesWorkspaceConstruction()
    {
        RecordingDocumentStore store = new();
        using KustoDocumentWorkspacePersistence persistence = new(store);
        int snapshotCount = 0;
        KustoDocumentWorkspace first = new([], null);
        KustoDocumentWorkspace latest = new(
            [new KustoDocument(Guid.NewGuid(), "Latest", "print 1", 0, null, null)],
            null);

        persistence.Schedule(() =>
        {
            Interlocked.Increment(ref snapshotCount);
            return first;
        });
        persistence.Schedule(() =>
        {
            Interlocked.Increment(ref snapshotCount);
            return latest;
        });

        Assert.Equal(0, Volatile.Read(ref snapshotCount));
        KustoDocumentWorkspace saved = await store.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, Volatile.Read(ref snapshotCount));
        Assert.Same(latest, saved);
    }

    private sealed class RecordingDocumentStore : IKustoDocumentStore
    {
        internal TaskCompletionSource<KustoDocumentWorkspace> Saved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public KustoDocumentWorkspace Load() => new([], null);

        public Task SaveAsync(
            KustoDocumentWorkspace workspace,
            CancellationToken cancellationToken = default)
        {
            _ = Saved.TrySetResult(workspace);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDocumentStore : IKustoDocumentStore
    {
        public KustoDocumentWorkspace Load() => new([], null);

        public Task SaveAsync(
            KustoDocumentWorkspace workspace,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException(new InvalidOperationException("Unexpected store failure"));
        }
    }
}
