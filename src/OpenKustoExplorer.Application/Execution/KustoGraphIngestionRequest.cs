using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Describes one validated graph query and the provenance used for its durable import.
/// </summary>
public sealed class KustoGraphIngestionRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphIngestionRequest"/> class.
    /// </summary>
    /// <param name="query">The selected query and target database.</param>
    /// <param name="plan">The validated graph export plan for the selected query.</param>
    /// <param name="importMode">Whether to add to or replace the active graph generation.</param>
    /// <param name="sourceKind">The workflow that initiated the query.</param>
    /// <param name="sourceId">The optional document or automation identifier.</param>
    /// <param name="sourceName">The analyst-facing document or automation name.</param>
    /// <param name="identityConflictResolver">The optional interactive duplicate-node resolver.</param>
    public KustoGraphIngestionRequest(
        KustoQueryRequest query,
        KustoGraphQueryPlan plan,
        GraphImportMode importMode,
        GraphIngestionSourceKind sourceKind,
        Guid? sourceId,
        string sourceName,
        GraphIdentityConflictResolver? identityConflictResolver = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceId), "The source identifier cannot be empty.");
        }

        if (!string.Equals(query.QueryText.Trim(), plan.Selection.Text, StringComparison.Ordinal))
        {
            throw new ArgumentException("The graph export plan does not describe the requested query.", nameof(plan));
        }

        Query = query;
        Plan = plan;
        ImportMode = importMode;
        SourceKind = sourceKind;
        SourceId = sourceId;
        SourceName = sourceName.Trim();
        IdentityConflictResolver = identityConflictResolver;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphIngestionRequest"/> class pinned to a named graph.
    /// </summary>
    /// <param name="query">The selected query and target database.</param>
    /// <param name="plan">The validated graph export plan for the selected query.</param>
    /// <param name="target">The graph and generation selected before remote execution begins.</param>
    /// <param name="importMode">Whether to add to or replace the pinned graph generation.</param>
    /// <param name="sourceKind">The workflow that initiated the query.</param>
    /// <param name="sourceId">The optional document or automation identifier.</param>
    /// <param name="sourceName">The analyst-facing document or automation name.</param>
    /// <param name="identityConflictResolver">The optional interactive duplicate-node resolver.</param>
    public KustoGraphIngestionRequest(
        KustoQueryRequest query,
        KustoGraphQueryPlan plan,
        GraphWriteTarget target,
        GraphImportMode importMode,
        GraphIngestionSourceKind sourceKind,
        Guid? sourceId,
        string sourceName,
        GraphIdentityConflictResolver? identityConflictResolver = null)
        : this(query, plan, importMode, sourceKind, sourceId, sourceName, identityConflictResolver)
    {
        Target = target;
    }

    /// <summary>
    /// Gets the durable graph import mode.
    /// </summary>
    public GraphImportMode ImportMode { get; }

    /// <summary>
    /// Gets the optional interactive duplicate-node resolver.
    /// </summary>
    public GraphIdentityConflictResolver? IdentityConflictResolver { get; }

    /// <summary>
    /// Gets the validated graph export plan.
    /// </summary>
    public KustoGraphQueryPlan Plan { get; }

    /// <summary>
    /// Gets the optional named graph target captured before remote execution.
    /// </summary>
    public GraphWriteTarget? Target { get; }

    /// <summary>
    /// Gets the selected query and target database.
    /// </summary>
    public KustoQueryRequest Query { get; }

    /// <summary>
    /// Gets the optional document or automation identifier.
    /// </summary>
    public Guid? SourceId { get; }

    /// <summary>
    /// Gets the workflow that initiated the query.
    /// </summary>
    public GraphIngestionSourceKind SourceKind { get; }

    /// <summary>
    /// Gets the analyst-facing document or automation name.
    /// </summary>
    public string SourceName { get; }
}
