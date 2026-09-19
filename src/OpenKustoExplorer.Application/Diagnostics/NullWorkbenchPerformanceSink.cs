namespace OpenKustoExplorer.Application.Diagnostics;

/// <summary>
/// Provides an allocation-free disabled workbench performance sink.
/// </summary>
public sealed class NullWorkbenchPerformanceSink : IWorkbenchPerformanceSink
{
    private NullWorkbenchPerformanceSink()
    {
    }

    /// <summary>Gets the shared disabled sink.</summary>
    public static NullWorkbenchPerformanceSink Instance { get; } = new();

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public long StartOperation(string operationName, int itemCount = 0)
    {
        _ = operationName;
        _ = itemCount;
        return 0;
    }

    /// <inheritdoc />
    public void CompleteOperation(long operationId, string outcome = "completed")
    {
        _ = operationId;
        _ = outcome;
    }

    /// <inheritdoc />
    public Task CompleteOperationAfterRenderAsync(
        long operationId,
        string outcome = "completed")
    {
        _ = operationId;
        _ = outcome;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Mark(string markerName)
    {
        _ = markerName;
    }
}
