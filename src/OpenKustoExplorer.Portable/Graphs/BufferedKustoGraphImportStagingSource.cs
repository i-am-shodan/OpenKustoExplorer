using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Portable.Graphs;

/// <summary>
/// Retains a bounded normalized graph export in memory for hosts without local database access.
/// </summary>
internal sealed class BufferedKustoGraphImportStagingSource : IKustoGraphImportStagingSource
{
    private readonly List<KustoGraphExportRow> edgeRows = [];
    private readonly Dictionary<string, GraphEntityKey> identityOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KustoGraphExportRow> nodesByHash = new(StringComparer.Ordinal);
    private readonly List<KustoGraphExportRow> nodeRows = [];
    private readonly KustoGraphExportNormalizer normalizer;
    private KustoGraphExportTableMetadata? currentTable;
    private KustoGraphExportTableMetadata? edgeTable;
    private GraphIngestion? ingestion;
    private bool isDisposed;
    private KustoGraphExportTableMetadata? nodeTable;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferedKustoGraphImportStagingSource"/> class.
    /// </summary>
    /// <param name="plan">The validated graph export plan.</param>
    /// <param name="query">The selected query and source database.</param>
    /// <param name="ingestionId">The identifier reserved for the ingestion.</param>
    internal BufferedKustoGraphImportStagingSource(
        KustoGraphQueryPlan plan,
        KustoQueryRequest query,
        Guid ingestionId)
    {
        normalizer = new KustoGraphExportNormalizer(plan, query, ingestionId);
    }

    /// <inheritdoc />
    public GraphIngestion Ingestion
    {
        get
        {
            EnsureReady();
            return ingestion!;
        }
    }

    /// <inheritdoc />
    public void Complete(GraphIngestion ingestion, KustoGraphExportSummary exportSummary)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(ingestion);
        ArgumentNullException.ThrowIfNull(exportSummary);
        if (this.ingestion is not null)
        {
            throw new InvalidOperationException("The graph staging source is already complete.");
        }

        if (currentTable is not null || nodeTable is null || edgeTable is null)
        {
            throw new InvalidDataException("Both graph export tables must finish before import.");
        }

        if (ingestion.Id != normalizer.IngestionId)
        {
            throw new ArgumentException(
                "The ingestion identifier does not match the staged export.",
                nameof(ingestion));
        }

        if (exportSummary.NodeCount != nodeRows.Count || exportSummary.EdgeCount != edgeRows.Count)
        {
            throw new InvalidDataException("The staged graph row counts do not match the validated export.");
        }

        int missingEndpointCount = edgeRows.Count(row =>
            !nodesByHash.ContainsKey(row.SourceHash) || !nodesByHash.ContainsKey(row.TargetHash));
        if (missingEndpointCount > 0)
        {
            throw new InvalidDataException(
                $"The graph export contains {missingEndpointCount:N0} edge rows with unresolved endpoints.");
        }

        this.ingestion = ingestion;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            edgeRows.Clear();
            identityOverrides.Clear();
            nodesByHash.Clear();
            nodeRows.Clear();
            currentTable = null;
            edgeTable = null;
            nodeTable = null;
            isDisposed = true;
        }
    }

    /// <inheritdoc />
    public IEnumerable<GraphEntityObservation> GetEntityObservations()
    {
        EnsureReady();
        return nodeRows
            .Select(row => normalizer.CreateEntityObservation(
                row,
                nodeTable!,
                ingestion!.CompletedAtUtc,
                GetEntity(row)))
            .ToArray();
    }

    /// <inheritdoc />
    public IEnumerable<GraphEvidence> GetEvidence()
    {
        EnsureReady();
        return nodeRows
            .Select(row => normalizer.CreateEvidence(row, nodeTable!))
            .Concat(edgeRows.Select(row => normalizer.CreateEvidence(row, edgeTable!)))
            .ToArray();
    }

    /// <inheritdoc />
    public IEnumerable<GraphEntityIdentityCandidate> GetIdentityCandidates()
    {
        EnsureReady();
        return CreateIdentityCandidates(nodeRows).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<GraphEntityIdentityConflict> GetIdentityConflicts()
    {
        EnsureReady();
        IEnumerable<(GraphEntityIdentityMatchKind Kind, string Value)> canonicalMatches = nodeRows
            .GroupBy(row => NormalizeMatchValue(GetEntity(row).CanonicalId), StringComparer.Ordinal)
            .Where(group => group.Select(GetEntity).Distinct().Skip(1).Any())
            .Select(group => (GraphEntityIdentityMatchKind.CanonicalId, group.Key));
        IEnumerable<(GraphEntityIdentityMatchKind Kind, string Value)> labelMatches = nodeRows
            .GroupBy(row => NormalizeMatchValue(row.DisplayLabel), StringComparer.Ordinal)
            .Where(group => group.Select(GetEntity).Distinct().Skip(1).Any())
            .Select(group => (GraphEntityIdentityMatchKind.DisplayLabel, group.Key));
        HashSet<string> candidateSets = new(StringComparer.Ordinal);
        List<GraphEntityIdentityConflict> conflicts = [];

        foreach ((GraphEntityIdentityMatchKind kind, string value) in canonicalMatches
            .Concat(labelMatches)
            .OrderBy(match => match.Kind)
            .ThenBy(match => match.Value, StringComparer.Ordinal))
        {
            KustoGraphExportRow[] matchingRows = nodeRows
                .Where(row => string.Equals(
                    NormalizeMatchValue(kind == GraphEntityIdentityMatchKind.CanonicalId
                        ? GetEntity(row).CanonicalId
                        : row.DisplayLabel),
                    value,
                    StringComparison.Ordinal))
                .ToArray();
            GraphEntityIdentityCandidate[] candidates = CreateIdentityCandidates(matchingRows).ToArray();
            string candidateSet = CreateCandidateSetKey(candidates);
            if (candidateSets.Add(candidateSet))
            {
                conflicts.Add(new GraphEntityIdentityConflict(
                    kind,
                    value,
                    candidates,
                    SelectSuggestedCandidate(candidates).Entity));
            }
        }

        return conflicts.AsReadOnly();
    }

    /// <inheritdoc />
    public IEnumerable<GraphRelationshipObservation> GetRelationshipObservations()
    {
        EnsureReady();
        return edgeRows
            .Select(row => normalizer.CreateRelationshipObservation(
                row,
                edgeTable!,
                GetEntity(nodesByHash[row.SourceHash]),
                GetEntity(nodesByHash[row.TargetHash]),
                ingestion!.CompletedAtUtc))
            .ToArray();
    }

    /// <inheritdoc />
    public void MergeIdentityConflict(GraphEntityIdentityConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        EnsureReady();
        if (!string.Equals(
            conflict.SuggestedEntity.SourceNamespace,
            normalizer.SourceNamespace,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The merged identity must use the staged source namespace.",
                nameof(conflict));
        }

        HashSet<GraphEntityKey> candidates = conflict.Candidates
            .Where(candidate => !candidate.IsExisting)
            .Select(candidate => candidate.Entity)
            .ToHashSet();
        foreach (KustoGraphExportRow row in nodeRows.Where(row => candidates.Contains(GetEntity(row))))
        {
            identityOverrides[row.NodeHash] = conflict.SuggestedEntity;
        }
    }

    /// <inheritdoc />
    public ValueTask WriteBatchAsync(
        KustoGraphExportBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();
        if (batch.StartsTable)
        {
            BeginTable(batch.Kind, batch.TableName!, batch.Columns);
        }

        foreach (IReadOnlyList<string> values in batch.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteRow(batch.Kind, values);
        }

        if (batch.EndsTable)
        {
            EndTable(batch.Kind);
        }

        return ValueTask.CompletedTask;
    }

    private static string CreateCandidateSetKey(IEnumerable<GraphEntityIdentityCandidate> candidates)
    {
        return string.Join(
            "\u001e",
            candidates
                .Select(candidate => $"{(int)candidate.Entity.Kind}\u001f{candidate.Entity}")
                .Order(StringComparer.Ordinal));
    }

    private static string NormalizeMatchValue(string value) => value.Trim().ToLowerInvariant();

    private static GraphEntityIdentityCandidate SelectSuggestedCandidate(
        IEnumerable<GraphEntityIdentityCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.IsExisting)
            .ThenByDescending(candidate => candidate.OccurrenceCount)
            .ThenBy(candidate => candidate.Entity.Kind == GraphEntityKind.Unknown)
            .ThenBy(candidate => !string.Equals(
                candidate.Entity.TypeName,
                candidate.Entity.Kind.ToString(),
                StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => string.Equals(
                candidate.Entity.TypeName,
                "Entity",
                StringComparison.Ordinal))
            .ThenBy(candidate => candidate.Entity.TypeName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private void BeginTable(
        KustoGraphExportTableKind kind,
        string tableName,
        IReadOnlyList<KustoResultColumn> columns)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (ingestion is not null)
        {
            throw new InvalidOperationException("The completed graph staging source cannot receive more tables.");
        }

        if (currentTable is not null)
        {
            throw new InvalidOperationException("A graph export table is already being staged.");
        }

        if ((kind == KustoGraphExportTableKind.Nodes && nodeTable is not null)
            || (kind == KustoGraphExportTableKind.Edges && edgeTable is not null))
        {
            throw new InvalidDataException($"Graph export table '{tableName}' was staged more than once.");
        }

        currentTable = normalizer.CreateTableMetadata(kind, tableName, columns);
    }

    private IEnumerable<GraphEntityIdentityCandidate> CreateIdentityCandidates(
        IEnumerable<KustoGraphExportRow> rows)
    {
        return rows
            .GroupBy(GetEntity)
            .Select(group => new GraphEntityIdentityCandidate(
                group.Key,
                group.Select(row => row.DisplayLabel).Order(StringComparer.Ordinal).First(),
                group.Count(),
                false))
            .OrderBy(candidate => candidate.Entity.TypeName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Entity.CanonicalId, StringComparer.Ordinal);
    }

    private void EndTable(KustoGraphExportTableKind kind)
    {
        KustoGraphExportTableMetadata metadata = GetCurrentTable(kind);
        if (kind == KustoGraphExportTableKind.Nodes)
        {
            nodeTable = metadata;
        }
        else
        {
            edgeTable = metadata;
        }

        currentTable = null;
    }

    private void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (ingestion is null || nodeTable is null || edgeTable is null)
        {
            throw new InvalidOperationException("The staged graph export has not completed validation.");
        }
    }

    private GraphEntityKey GetEntity(KustoGraphExportRow row)
    {
        return identityOverrides.TryGetValue(row.NodeHash, out GraphEntityKey entity)
            ? entity
            : normalizer.CreateEntityKey(row);
    }

    private KustoGraphExportTableMetadata GetCurrentTable(KustoGraphExportTableKind kind)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (currentTable is null || currentTable.Kind != kind)
        {
            throw new InvalidOperationException($"No {kind} graph export table is currently being staged.");
        }

        return currentTable;
    }

    private void WriteRow(KustoGraphExportTableKind kind, IReadOnlyList<string> values)
    {
        KustoGraphExportTableMetadata metadata = GetCurrentTable(kind);
        List<KustoGraphExportRow> rows = kind == KustoGraphExportTableKind.Nodes
            ? nodeRows
            : edgeRows;
        int maximumRowCount = kind == KustoGraphExportTableKind.Nodes
            ? GraphStorageLimits.MaximumEntityCount
            : GraphStorageLimits.MaximumRelationshipCount;
        if (rows.Count >= maximumRowCount)
        {
            throw new InvalidDataException(
                $"The graph export exceeds the {maximumRowCount:N0} {kind.ToString().ToLowerInvariant()} limit.");
        }

        KustoGraphExportRow row = normalizer.NormalizeRow(metadata, values, rows.Count);
        if (kind == KustoGraphExportTableKind.Nodes && !nodesByHash.TryAdd(row.NodeHash, row))
        {
            throw new InvalidDataException($"The graph export contains duplicate node hash '{row.NodeHash}'.");
        }

        rows.Add(row);
    }
}
