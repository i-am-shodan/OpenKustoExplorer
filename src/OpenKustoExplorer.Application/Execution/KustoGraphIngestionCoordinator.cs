using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Executes, validates, stages, resolves, and atomically imports Kusto graph exports.
/// </summary>
public sealed class KustoGraphIngestionCoordinator : IKustoGraphIngestionService
{
    private readonly IKustoGraphQueryService graphQueryService;
    private readonly IKustoGraphImportStagingSourceFactory stagingSourceFactory;
    private readonly IGraphStore graphStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphIngestionCoordinator"/> class.
    /// </summary>
    /// <param name="graphQueryService">The streaming Kusto graph query service.</param>
    /// <param name="graphStore">The app-wide durable graph store.</param>
    /// <param name="stagingSourceFactory">The host-appropriate staging source factory.</param>
    public KustoGraphIngestionCoordinator(
        IKustoGraphQueryService graphQueryService,
        IGraphStore graphStore,
        IKustoGraphImportStagingSourceFactory stagingSourceFactory)
    {
        ArgumentNullException.ThrowIfNull(graphQueryService);
        ArgumentNullException.ThrowIfNull(graphStore);
        ArgumentNullException.ThrowIfNull(stagingSourceFactory);

        this.graphQueryService = graphQueryService;
        this.graphStore = graphStore;
        this.stagingSourceFactory = stagingSourceFactory;
    }

    /// <inheritdoc />
    public async Task<KustoGraphIngestionResult> ExecuteAsync(
        KustoGraphIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        Guid ingestionId = Guid.NewGuid();
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        using IKustoGraphImportStagingSource stagingSource = stagingSourceFactory.Create(
            request.Plan,
            request.Query,
            ingestionId);
        KustoGraphExportSummary export = await graphQueryService.ExecuteGraphAsync(
            request.Query,
            request.Plan,
            stagingSource,
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;

        if (completedAtUtc < startedAtUtc)
        {
            completedAtUtc = startedAtUtc;
        }

        GraphIngestion ingestion = new(
            ingestionId,
            request.SourceKind,
            request.SourceId,
            request.SourceName,
            request.Query.ClusterUri,
            request.Query.DatabaseName,
            request.Query.QueryText,
            startedAtUtc,
            completedAtUtc);
        stagingSource.Complete(ingestion, export);
        cancellationToken.ThrowIfCancellationRequested();
        await ResolveStagedIdentityConflictsAsync(
            stagingSource,
            request.IdentityConflictResolver,
            cancellationToken).ConfigureAwait(false);
        if (request.ImportMode == GraphImportMode.Add && request.IdentityConflictResolver is not null)
        {
            GraphSnapshot targetSnapshot = request.Target?.Snapshot
                ?? (await graphStore.GetStateAsync(cancellationToken).ConfigureAwait(false)).Snapshot;
            IReadOnlyList<GraphEntityIdentityConflict> targetConflicts = await graphStore
                .FindIdentityConflictsAsync(
                    targetSnapshot,
                    stagingSource.GetIdentityCandidates(),
                    cancellationToken)
                .ConfigureAwait(false);
            await ResolveTargetIdentityConflictsAsync(
                stagingSource,
                targetConflicts,
                request.IdentityConflictResolver,
                cancellationToken).ConfigureAwait(false);
        }

        GraphImportResult import = request.Target is GraphWriteTarget target
            ? await graphStore.ImportAsync(
                target,
                stagingSource,
                request.ImportMode,
                cancellationToken).ConfigureAwait(false)
            : await graphStore.ImportAsync(
                stagingSource,
                request.ImportMode,
                cancellationToken).ConfigureAwait(false);

        return new KustoGraphIngestionResult(export, import);
    }

    private static string CreateConflictKey(GraphEntityIdentityConflict conflict)
    {
        return string.Join(
            "\u001e",
            conflict.Candidates
                .Select(candidate => $"{(int)candidate.Entity.Kind}\u001f{candidate.Entity}")
                .Order(StringComparer.Ordinal));
    }

    private static async Task ResolveStagedIdentityConflictsAsync(
        IKustoGraphImportStagingSource stagingSource,
        GraphIdentityConflictResolver? resolver,
        CancellationToken cancellationToken)
    {
        if (resolver is null)
        {
            return;
        }

        HashSet<string> preservedConflicts = new(StringComparer.Ordinal);
        GraphEntityIdentityConflict? conflict;
        do
        {
            conflict = stagingSource.GetIdentityConflicts()
                .FirstOrDefault(candidate => !preservedConflicts.Contains(CreateConflictKey(candidate)));
            if (conflict is not null)
            {
                GraphIdentityResolutionDecision decision = await resolver(conflict, cancellationToken)
                    .ConfigureAwait(false);
                switch (decision)
                {
                    case GraphIdentityResolutionDecision.Merge:
                        stagingSource.MergeIdentityConflict(conflict);
                        break;
                    case GraphIdentityResolutionDecision.KeepSeparate:
                        preservedConflicts.Add(CreateConflictKey(conflict));
                        break;
                    case GraphIdentityResolutionDecision.Cancel:
                        throw new GraphIdentityResolutionCanceledException();
                    default:
                        throw new InvalidOperationException("The graph identity resolution is not supported.");
                }
            }
        }
        while (conflict is not null);
    }

    private static async Task ResolveTargetIdentityConflictsAsync(
        IKustoGraphImportStagingSource stagingSource,
        IEnumerable<GraphEntityIdentityConflict> conflicts,
        GraphIdentityConflictResolver resolver,
        CancellationToken cancellationToken)
    {
        foreach (GraphEntityIdentityConflict conflict in conflicts)
        {
            GraphIdentityResolutionDecision decision = await resolver(conflict, cancellationToken)
                .ConfigureAwait(false);
            switch (decision)
            {
                case GraphIdentityResolutionDecision.Merge:
                    stagingSource.MergeIdentityConflict(conflict);
                    break;
                case GraphIdentityResolutionDecision.KeepSeparate:
                    break;
                case GraphIdentityResolutionDecision.Cancel:
                    throw new GraphIdentityResolutionCanceledException();
                default:
                    throw new InvalidOperationException("The graph identity resolution is not supported.");
            }
        }
    }
}
