using OpenKustoExplorer.Application.Diagnostics;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Shares one latest KQL analysis across editor diagnostics and completion consumers.
/// </summary>
internal sealed class KustoEditorAnalysisCoordinator : IDisposable
{
    private readonly Func<string, int, CancellationToken, KustoLanguageAnalysis> analyze;
    private readonly IWorkbenchPerformanceSink performanceSink;
    private readonly Lock synchronizationRoot = new();
    private CancellationTokenSource? activeCancellationSource;
    private AnalysisKey activeKey;
    private Task<KustoLanguageAnalysis>? activeTask;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoEditorAnalysisCoordinator"/> class.
    /// </summary>
    /// <param name="analyze">The synchronous snapshot analysis function.</param>
    /// <param name="performanceSink">The optional detailed performance sink.</param>
    public KustoEditorAnalysisCoordinator(
        Func<string, int, CancellationToken, KustoLanguageAnalysis> analyze,
        IWorkbenchPerformanceSink? performanceSink = null)
    {
        ArgumentNullException.ThrowIfNull(analyze);
        this.analyze = analyze;
        this.performanceSink = performanceSink ?? NullWorkbenchPerformanceSink.Instance;
    }

    /// <summary>
    /// Gets or starts analysis for one immutable editor snapshot.
    /// </summary>
    /// <param name="documentId">The selected document identifier.</param>
    /// <param name="text">The complete document text.</param>
    /// <param name="caretPosition">The analysis caret position.</param>
    /// <param name="schemaRevision">The selected schema revision.</param>
    /// <param name="cancellationToken">Cancels this consumer's wait.</param>
    /// <returns>The shared analysis result.</returns>
    public Task<KustoLanguageAnalysis> GetAnalysisAsync(
        Guid documentId,
        string text,
        int caretPosition,
        int schemaRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caretPosition);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caretPosition, text.Length);
        cancellationToken.ThrowIfCancellationRequested();
        AnalysisKey key = new(documentId, text, caretPosition, schemaRevision);
        Task<KustoLanguageAnalysis> task;

        lock (synchronizationRoot)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (activeTask is null
                || activeKey != key
                || activeTask.IsCanceled
                || activeTask.IsFaulted)
            {
                activeCancellationSource?.Cancel();
                activeCancellationSource?.Dispose();
                activeCancellationSource = new CancellationTokenSource();
                activeKey = key;
                activeTask = AnalyzeAsync(key, activeCancellationSource.Token);
            }

            task = activeTask;
        }

        return task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (synchronizationRoot)
        {
            if (!isDisposed)
            {
                isDisposed = true;
                activeCancellationSource?.Cancel();
                activeCancellationSource?.Dispose();
                activeCancellationSource = null;
                activeTask = null;
            }
        }
    }

    private async Task<KustoLanguageAnalysis> AnalyzeAsync(
        AnalysisKey key,
        CancellationToken cancellationToken)
    {
        long operationId = performanceSink.StartOperation(
            "editor.analysis.compute",
            key.Text.Length);
        try
        {
            KustoLanguageAnalysis result = await Task.Run(
                () => analyze(key.Text, key.CaretPosition, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            performanceSink.CompleteOperation(operationId);
            return result;
        }
        catch (OperationCanceledException)
        {
            performanceSink.CompleteOperation(operationId, "canceled");
            throw;
        }
        catch
        {
            performanceSink.CompleteOperation(operationId, "failed");
            throw;
        }
    }

    private readonly record struct AnalysisKey(
        Guid DocumentId,
        string Text,
        int CaretPosition,
        int SchemaRevision);
}
