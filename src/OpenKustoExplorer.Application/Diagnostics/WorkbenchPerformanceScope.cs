namespace OpenKustoExplorer.Application.Diagnostics;

/// <summary>
/// Completes one correlated workbench operation when disposed.
/// </summary>
public readonly struct WorkbenchPerformanceScope : IDisposable
{
    private readonly IWorkbenchPerformanceSink sink;
    private readonly long operationId;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkbenchPerformanceScope"/> struct.
    /// </summary>
    /// <param name="sink">The destination performance sink.</param>
    /// <param name="operationName">The stable operation name.</param>
    /// <param name="itemCount">The optional number of items processed.</param>
    public WorkbenchPerformanceScope(
        IWorkbenchPerformanceSink sink,
        string operationName,
        int itemCount = 0)
    {
        ArgumentNullException.ThrowIfNull(sink);
        this.sink = sink;
        operationId = sink.StartOperation(operationName, itemCount);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        sink.CompleteOperation(operationId);
    }
}
