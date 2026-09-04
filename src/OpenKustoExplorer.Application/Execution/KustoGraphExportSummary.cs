namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Reports graph export duration and streamed row counts without materializing rows.
/// </summary>
public sealed class KustoGraphExportSummary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphExportSummary"/> class.
    /// </summary>
    /// <param name="nodeCount">The number of streamed node rows.</param>
    /// <param name="edgeCount">The number of streamed edge rows.</param>
    /// <param name="duration">The measured query and response duration.</param>
    /// <param name="resultTable">The optional bounded edge table for the Results view.</param>
    public KustoGraphExportSummary(
        long nodeCount,
        long edgeCount,
        TimeSpan duration,
        KustoResultTable? resultTable = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(edgeCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        NodeCount = nodeCount;
        EdgeCount = edgeCount;
        Duration = duration;
        ResultTable = resultTable;
    }

    /// <summary>
    /// Gets the number of streamed node rows.
    /// </summary>
    public long NodeCount { get; }

    /// <summary>
    /// Gets the number of streamed edge rows.
    /// </summary>
    public long EdgeCount { get; }

    /// <summary>
    /// Gets the measured query and response duration.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the bounded graph edge table for the Results view, when materialized.
    /// </summary>
    public KustoResultTable? ResultTable { get; }
}
