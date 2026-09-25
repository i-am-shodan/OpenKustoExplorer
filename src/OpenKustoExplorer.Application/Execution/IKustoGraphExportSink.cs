namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Receives bounded graph export batches without retaining the complete query response in memory.
/// </summary>
public interface IKustoGraphExportSink
{
    /// <summary>
    /// Writes one ordered graph export batch.
    /// </summary>
    /// <param name="batch">The table boundary metadata and ordered rows.</param>
    /// <param name="cancellationToken">A token that cancels delivery.</param>
    /// <returns>A value task that completes when the sink has accepted the batch.</returns>
    public ValueTask WriteBatchAsync(
        KustoGraphExportBatch batch,
        CancellationToken cancellationToken = default);
}
