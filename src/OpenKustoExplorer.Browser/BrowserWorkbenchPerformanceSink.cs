using System.Globalization;
using OpenKustoExplorer.Application.Diagnostics;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Records correlated workbench operations in the Browser Performance Timeline.
/// </summary>
internal sealed class BrowserWorkbenchPerformanceSink : IWorkbenchPerformanceSink
{
    private long nextOperationId;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public long StartOperation(string operationName, int itemCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        long operationId = Interlocked.Increment(ref nextOperationId);
        BrowserInterop.StartPerformanceOperation(
            operationId.ToString(CultureInfo.InvariantCulture),
            operationName,
            itemCount);
        return operationId;
    }

    /// <inheritdoc />
    public void CompleteOperation(long operationId, string outcome = "completed")
    {
        if (operationId > 0)
        {
            BrowserInterop.CompletePerformanceOperation(
                operationId.ToString(CultureInfo.InvariantCulture),
                outcome);
        }
    }

    /// <inheritdoc />
    public Task CompleteOperationAfterRenderAsync(
        long operationId,
        string outcome = "completed")
    {
        return operationId > 0
            ? BrowserInterop.CompletePerformanceOperationAfterRenderAsync(
                operationId.ToString(CultureInfo.InvariantCulture),
                outcome)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Mark(string markerName)
    {
        BrowserInterop.MarkPerformance(markerName);
    }
}
