using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Desktop.Editor;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies shared editor analysis computation and cancellation.
/// </summary>
public sealed class KustoEditorAnalysisCoordinatorTests
{
    /// <summary>
    /// Verifies concurrent and later consumers reuse one successful snapshot analysis.
    /// </summary>
    /// <returns>A task that completes after all consumers receive the shared result.</returns>
    [Fact]
    public async Task SameSnapshotSharesInFlightAndCompletedAnalysis()
    {
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim release = new();
        int analysisCount = 0;
        using KustoEditorAnalysisCoordinator coordinator = new(
            (text, caret, cancellationToken) =>
            {
                _ = text;
                _ = caret;
                Interlocked.Increment(ref analysisCount);
                started.Set();
                release.Wait(cancellationToken);
                return new KustoLanguageAnalysis([], [], [], 0, 0);
            });
        Guid documentId = Guid.NewGuid();

        Task<KustoLanguageAnalysis> first = coordinator.GetAnalysisAsync(
            documentId,
            "StormEvents",
            11,
            schemaRevision: 3,
            CancellationToken.None);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Task<KustoLanguageAnalysis> second = coordinator.GetAnalysisAsync(
            documentId,
            "StormEvents",
            11,
            schemaRevision: 3,
            CancellationToken.None);
        release.Set();
        KustoLanguageAnalysis[] concurrent = await Task.WhenAll(first, second);
        KustoLanguageAnalysis cached = await coordinator.GetAnalysisAsync(
            documentId,
            "StormEvents",
            11,
            schemaRevision: 3,
            CancellationToken.None);

        Assert.Equal(1, analysisCount);
        Assert.Same(concurrent[0], concurrent[1]);
        Assert.Same(concurrent[0], cached);
    }

    /// <summary>
    /// Verifies a newer snapshot cancels obsolete computation before starting replacement work.
    /// </summary>
    /// <returns>A task that completes after supersession is observed.</returns>
    [Fact]
    public async Task NewSnapshotCancelsObsoleteAnalysis()
    {
        using ManualResetEventSlim firstStarted = new();
        using KustoEditorAnalysisCoordinator coordinator = new(
            (text, caret, cancellationToken) =>
            {
                _ = caret;
                if (text == "first")
                {
                    firstStarted.Set();
                    cancellationToken.WaitHandle.WaitOne();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return new KustoLanguageAnalysis([], [], [], 0, 0);
            });
        Guid documentId = Guid.NewGuid();
        Task<KustoLanguageAnalysis> obsolete = coordinator.GetAnalysisAsync(
            documentId,
            "first",
            5,
            schemaRevision: 1,
            CancellationToken.None);
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));

        KustoLanguageAnalysis replacement = await coordinator.GetAnalysisAsync(
            documentId,
            "second",
            6,
            schemaRevision: 1,
            CancellationToken.None);

        Assert.NotNull(replacement);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => obsolete);
    }
}
