using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Reports the streamed Kusto export and atomic durable graph import results.
/// </summary>
public sealed class KustoGraphIngestionResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphIngestionResult"/> class.
    /// </summary>
    /// <param name="export">The streamed graph export summary.</param>
    /// <param name="import">The committed graph import summary.</param>
    public KustoGraphIngestionResult(KustoGraphExportSummary export, GraphImportResult import)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(import);

        Export = export;
        Import = import;
    }

    /// <summary>
    /// Gets the streamed graph export summary.
    /// </summary>
    public KustoGraphExportSummary Export { get; }

    /// <summary>
    /// Gets the committed graph import summary.
    /// </summary>
    public GraphImportResult Import { get; }
}
