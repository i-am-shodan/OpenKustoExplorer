using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Stages streamed Kusto graph rows until a validated export can be imported atomically.
/// </summary>
public interface IKustoGraphImportStagingSource : IGraphImportSource, IKustoGraphExportSink, IDisposable
{
    /// <summary>
    /// Marks all staged rows as a complete, validated ingestion.
    /// </summary>
    /// <param name="ingestion">The completed query provenance.</param>
    /// <param name="exportSummary">The validated export summary.</param>
    public void Complete(GraphIngestion ingestion, KustoGraphExportSummary exportSummary);

    /// <summary>
    /// Gets distinct staged identities for target-graph duplicate preflight.
    /// </summary>
    /// <returns>The staged identity candidates.</returns>
    public IEnumerable<GraphEntityIdentityCandidate> GetIdentityCandidates();

    /// <summary>
    /// Gets identity conflicts contained within the staged export.
    /// </summary>
    /// <returns>The distinct identity conflicts.</returns>
    public IReadOnlyList<GraphEntityIdentityConflict> GetIdentityConflicts();

    /// <summary>
    /// Remaps staged candidates in one approved conflict to its suggested identity.
    /// </summary>
    /// <param name="conflict">The approved identity conflict.</param>
    public void MergeIdentityConflict(GraphEntityIdentityConflict conflict);
}
