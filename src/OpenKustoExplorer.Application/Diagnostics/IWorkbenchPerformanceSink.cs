namespace OpenKustoExplorer.Application.Diagnostics;

/// <summary>
/// Records correlated workbench operations without retaining application data.
/// </summary>
public interface IWorkbenchPerformanceSink
{
    /// <summary>Gets a value indicating whether detailed profiling is enabled.</summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Starts one correlated operation.
    /// </summary>
    /// <param name="operationName">The stable operation name.</param>
    /// <param name="itemCount">The optional number of items processed.</param>
    /// <returns>The correlation identifier, or zero when profiling is disabled.</returns>
    public long StartOperation(string operationName, int itemCount = 0);

    /// <summary>
    /// Completes one operation immediately.
    /// </summary>
    /// <param name="operationId">The identifier returned by <see cref="StartOperation"/>.</param>
    /// <param name="outcome">The bounded operation outcome.</param>
    public void CompleteOperation(long operationId, string outcome = "completed");

    /// <summary>
    /// Completes one operation after the host has had an opportunity to paint.
    /// </summary>
    /// <param name="operationId">The identifier returned by <see cref="StartOperation"/>.</param>
    /// <param name="outcome">The bounded operation outcome.</param>
    /// <returns>A task that completes after the operation is recorded.</returns>
    public Task CompleteOperationAfterRenderAsync(
        long operationId,
        string outcome = "completed");

    /// <summary>
    /// Records one instantaneous workbench milestone.
    /// </summary>
    /// <param name="markerName">The stable marker name.</param>
    public void Mark(string markerName);
}
