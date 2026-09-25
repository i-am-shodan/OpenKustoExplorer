using System.Text.Json;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;

#pragma warning disable SA1600

namespace OpenKustoExplorer.Portable.Graphs;

/// <summary>
/// Persists complete investigation graphs as one versioned JSON snapshot using host-provided storage.
/// </summary>
public sealed class JsonGraphStore : IGraphStore, IGraphQueryService, IDisposable
{
    private const string DefaultGraphName = "Default graph";
    private readonly IGraphSnapshotStore snapshotStore;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private GraphSnapshotDocument snapshot;
    private bool isDisposed;

    private JsonGraphStore(
        IGraphSnapshotStore snapshotStore,
        GraphSnapshotDocument snapshot,
        TimeProvider timeProvider)
    {
        this.snapshotStore = snapshotStore;
        this.snapshot = snapshot;
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// Loads a portable graph store from host-provided durable storage.
    /// </summary>
    /// <param name="snapshotStore">The host-specific durable snapshot store.</param>
    /// <param name="timeProvider">The clock used for graph metadata.</param>
    /// <param name="invalidSnapshotHandler">Optionally preserves an invalid snapshot before recovery.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The initialized graph store.</returns>
    public static async Task<JsonGraphStore> CreateAsync(
        IGraphSnapshotStore snapshotStore,
        TimeProvider? timeProvider = null,
        Func<string, CancellationToken, Task>? invalidSnapshotHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotStore);
        TimeProvider clock = timeProvider ?? TimeProvider.System;
        string? json = await snapshotStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        GraphSnapshotDocument initialSnapshot = DeserializeOrCreate(
            json,
            clock.GetUtcNow(),
            out bool invalidSnapshot);
        if (invalidSnapshot && invalidSnapshotHandler is not null)
        {
            await invalidSnapshotHandler(json!, cancellationToken).ConfigureAwait(false);
        }

        return new JsonGraphStore(snapshotStore, initialSnapshot, clock);
    }

    /// <inheritdoc />
    public Task<GraphCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(CreateCatalog, cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphStateSummary> CreateGraphAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = GraphCatalogMetadata.NormalizeName(name);
        string normalizedNameKey = GraphCatalogMetadata.NormalizeNameKey(name);
        string normalizedDescription = GraphCatalogMetadata.NormalizeDescription(description);
        return ExecuteWriteAsync(
            document =>
            {
                if (document.Graphs.Any(graph => string.Equals(
                    GraphCatalogMetadata.NormalizeNameKey(graph.Name),
                    normalizedNameKey,
                    StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("A graph with this name already exists.");
                }

                DateTimeOffset utcNow = timeProvider.GetUtcNow();
                GraphDocument graph = CreateGraphDocument(normalizedName, normalizedDescription, utcNow);
                document.Graphs.Add(graph);
                document.ActiveGraphId = graph.Id;
                return CreateState(graph, GetActiveGeneration(graph));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphCatalogEntry> UpdateGraphAsync(
        Guid graphId,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        string normalizedName = GraphCatalogMetadata.NormalizeName(name);
        string normalizedNameKey = GraphCatalogMetadata.NormalizeNameKey(name);
        string normalizedDescription = GraphCatalogMetadata.NormalizeDescription(description);
        return ExecuteWriteAsync(
            document =>
            {
                GraphDocument graph = GetGraph(document, graphId);
                if (document.Graphs.Any(candidate => candidate.Id != graphId && string.Equals(
                    GraphCatalogMetadata.NormalizeNameKey(candidate.Name),
                    normalizedNameKey,
                    StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("A graph with this name already exists.");
                }

                graph.Name = normalizedName;
                graph.Description = normalizedDescription;
                graph.LastUpdatedAtUtc = LaterOf(graph.LastUpdatedAtUtc, timeProvider.GetUtcNow());
                return CreateCatalogEntry(graph);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphEntitySummary> SetEntityDisplayLabelAsync(
        GraphSnapshot snapshot,
        GraphEntityKey entity,
        string displayLabel,
        CancellationToken cancellationToken = default)
    {
        string normalizedLabel = GraphEntityDisplayLabel.Normalize(displayLabel);
        return ExecuteWriteAsync(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, snapshot);
                GraphSnapshotProjection projection = GraphSnapshotProjection.Create(graph, generation);
                if (!projection.Entities.ContainsKey(entity))
                {
                    throw new InvalidOperationException("The graph node does not exist in the selected graph.");
                }

                graph.LabelOverrides.RemoveAll(
                    candidate => GraphSnapshotMapper.ToEntityKey(candidate.Entity) == entity);
                graph.LabelOverrides.Add(new GraphLabelOverrideDocument
                {
                    Entity = ToDocument(entity),
                    DisplayLabel = normalizedLabel,
                });
                graph.LastUpdatedAtUtc = LaterOf(graph.LastUpdatedAtUtc, timeProvider.GetUtcNow());
                return GraphSnapshotProjection.Create(graph, generation).CreateSummary(entity);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphStateSummary> ActivateGraphAsync(
        Guid graphId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        return ExecuteWriteAsync(
            document =>
            {
                GraphDocument graph = GetGraph(document, graphId);
                document.ActiveGraphId = graph.Id;
                graph.LastActivatedAtUtc = LaterOf(graph.CreatedAtUtc, timeProvider.GetUtcNow());
                return CreateState(graph, GetActiveGeneration(graph));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphCatalog> DeleteGraphAsync(
        Guid graphId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        return ExecuteWriteAsync(
            document =>
            {
                if (document.Graphs.Count == 1)
                {
                    throw new InvalidOperationException("The final saved graph cannot be deleted.");
                }

                int removed = document.Graphs.RemoveAll(graph => graph.Id == graphId);
                if (removed == 0)
                {
                    throw new InvalidOperationException("The graph does not exist.");
                }

                if (document.ActiveGraphId == graphId)
                {
                    GraphDocument replacement = document.Graphs
                        .OrderByDescending(graph => graph.LastActivatedAtUtc)
                        .ThenBy(graph => graph.Name, StringComparer.OrdinalIgnoreCase)
                        .First();
                    document.ActiveGraphId = replacement.Id;
                    replacement.LastActivatedAtUtc = LaterOf(
                        replacement.CreatedAtUtc,
                        timeProvider.GetUtcNow());
                }

                return CreateCatalog(document);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphCatalog> DeleteAllGraphsAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            document =>
            {
                GraphSnapshotDocument replacement = CreateDefaultSnapshot(timeProvider.GetUtcNow());
                document.ActiveGraphId = replacement.ActiveGraphId;
                document.Graphs = replacement.Graphs;
                return CreateCatalog(document);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphStateSummary> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            document =>
            {
                GraphDocument graph = GetActiveGraph(document);
                return CreateState(graph, GetActiveGeneration(graph));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphStateSummary> GetStateAsync(
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, snapshot);
                return CreateState(graph, generation);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphEntityDetails?> GetEntityDetailsAsync(
        GraphEntityKey entity,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            document =>
            {
                GraphDocument graph = GetActiveGraph(document);
                return GraphSnapshotProjection.Create(graph, GetActiveGeneration(graph)).GetEntityDetails(entity);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphEntityDetails?> GetEntityDetailsAsync(
        GraphSnapshot snapshot,
        GraphEntityKey entity,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, snapshot);
                return GraphSnapshotProjection.Create(graph, generation).GetEntityDetails(entity);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphRelationshipDetails?> GetRelationshipDetailsAsync(
        GraphRelationshipKey relationship,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            document =>
            {
                GraphDocument graph = GetActiveGraph(document);
                return GraphSnapshotProjection.Create(graph, GetActiveGeneration(graph))
                    .GetRelationshipDetails(relationship);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphRelationshipDetails?> GetRelationshipDetailsAsync(
        GraphSnapshot snapshot,
        GraphRelationshipKey relationship,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, snapshot);
                return GraphSnapshotProjection.Create(graph, generation).GetRelationshipDetails(relationship);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphTimelinePoint>> GetTimelineAsync(
        Guid graphId,
        int maximumPoints,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPoints);
        return ExecuteReadAsync<IReadOnlyList<GraphTimelinePoint>>(
            document =>
            {
                GraphDocument graph = GetGraph(document, graphId);
                return graph.Generations
                    .SelectMany(generation => CreateTimeline(graph, generation))
                    .OrderByDescending(point => point.TimestampUtc)
                    .ThenByDescending(point => point.IngestionId)
                    .Take(maximumPoints)
                    .ToArray();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphViewport> GetTimelineViewportAsync(
        GraphTimelinePoint point,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateViewportLimits(maximumEntityCount, maximumRelationshipCount);
        return ExecuteReadAsync(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, point.Snapshot);
                GraphGenerationDocument visibleGeneration = point.IngestionId is null
                    ? new GraphGenerationDocument
                    {
                        Id = generation.Id,
                        CreatedAtUtc = generation.CreatedAtUtc,
                        LastUpdatedAtUtc = generation.CreatedAtUtc,
                    }
                    : generation;
                GraphSnapshotProjection projection = GraphSnapshotProjection.Create(
                    graph,
                    visibleGeneration,
                    point.IngestionId);
                return projection.GetViewport(
                    null,
                    maximumEntityCount,
                    maximumRelationshipCount,
                    cancellationToken);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphImportResult> ImportAsync(
        IGraphImportSource source,
        GraphImportMode mode,
        CancellationToken cancellationToken = default)
    {
        return ImportCoreAsync(null, source, mode, cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphImportResult> ImportAsync(
        GraphWriteTarget target,
        IGraphImportSource source,
        GraphImportMode mode,
        CancellationToken cancellationToken = default)
    {
        return ImportCoreAsync(target, source, mode, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphEntityIdentityConflict>> FindIdentityConflictsAsync(
        GraphSnapshot snapshot,
        IEnumerable<GraphEntityIdentityCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        GraphEntityIdentityCandidate[] candidateSnapshot = candidates.ToArray();
        return ExecuteReadAsync<IReadOnlyList<GraphEntityIdentityConflict>>(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, snapshot);
                GraphSnapshotProjection projection = GraphSnapshotProjection.Create(graph, generation);
                List<GraphEntityIdentityConflict> conflicts = [];
                foreach (GraphEntityIdentityCandidate candidate in candidateSnapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    GraphEntitySummary[] matches = projection.Entities.Keys
                        .Where(entity => entity != candidate.Entity
                            && string.Equals(
                                entity.SourceNamespace,
                                candidate.Entity.SourceNamespace,
                                StringComparison.Ordinal))
                        .Select(projection.CreateSummary)
                        .Where(summary => string.Equals(
                                summary.Entity.CanonicalId.Trim(),
                                candidate.Entity.CanonicalId.Trim(),
                                StringComparison.OrdinalIgnoreCase)
                            || string.Equals(
                                summary.DisplayLabel.Trim(),
                                candidate.DisplayLabel.Trim(),
                                StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (matches.Length == 0)
                    {
                        continue;
                    }

                    GraphEntityIdentityMatchKind matchKind = matches.Any(summary => string.Equals(
                        summary.Entity.CanonicalId.Trim(),
                        candidate.Entity.CanonicalId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                        ? GraphEntityIdentityMatchKind.CanonicalId
                        : GraphEntityIdentityMatchKind.DisplayLabel;
                    GraphEntityIdentityCandidate[] group =
                    [
                        candidate,
                        .. matches.Select(summary => new GraphEntityIdentityCandidate(
                            summary.Entity,
                            summary.DisplayLabel,
                            1,
                            true)),
                    ];
                    string matchValue = matchKind == GraphEntityIdentityMatchKind.CanonicalId
                        ? candidate.Entity.CanonicalId
                        : candidate.DisplayLabel;
                    conflicts.Add(new GraphEntityIdentityConflict(
                        matchKind,
                        matchValue,
                        group,
                        matches[0].Entity));
                }

                return conflicts;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphEntitySummary>> SearchEntitiesAsync(
        string searchText,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateSearch(searchText, maximumResults);
        return ExecuteReadAsync<IReadOnlyList<GraphEntitySummary>>(
            document =>
            {
                GraphDocument graph = GetActiveGraph(document);
                return GraphSnapshotProjection.Create(graph, GetActiveGeneration(graph))
                    .Search(searchText, maximumResults);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphEntitySummary>> SearchEntitiesAsync(
        GraphSnapshot snapshot,
        string searchText,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateSearch(searchText, maximumResults);
        return ExecuteReadAsync<IReadOnlyList<GraphEntitySummary>>(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, snapshot);
                return GraphSnapshotProjection.Create(graph, generation).Search(searchText, maximumResults);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphViewport> GetViewportAsync(
        GraphEntityKey? center,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ValidateViewportLimits(maximumEntityCount, maximumRelationshipCount);
        return ExecuteReadAsync(
            document =>
            {
                GraphDocument graph = GetActiveGraph(document);
                return GraphSnapshotProjection.Create(graph, GetActiveGeneration(graph)).GetViewport(
                    center,
                    maximumEntityCount,
                    maximumRelationshipCount,
                    cancellationToken);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphViewport> GetViewportAsync(
        GraphSnapshot snapshot,
        GraphEntityKey? center,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ValidatePositiveViewportLimits(maximumEntityCount, maximumRelationshipCount);
        return ExecuteReadAsync(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, snapshot);
                return GraphSnapshotProjection.Create(graph, generation).GetViewport(
                    center,
                    maximumEntityCount,
                    maximumRelationshipCount,
                    cancellationToken);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphViewport> GetNeighborhoodAsync(
        IReadOnlyCollection<GraphEntityKey> centers,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ValidateNeighborhoodLimits(centers, maximumDepth, maximumEntityCount, maximumRelationshipCount);
        return ExecuteReadAsync(
            document =>
            {
                GraphDocument graph = GetActiveGraph(document);
                return GraphSnapshotProjection.Create(graph, GetActiveGeneration(graph)).GetNeighborhood(
                    centers,
                    maximumDepth,
                    maximumEntityCount,
                    maximumRelationshipCount,
                    cancellationToken);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphViewport> GetNeighborhoodAsync(
        GraphSnapshot snapshot,
        IReadOnlyCollection<GraphEntityKey> centers,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ValidateNeighborhoodLimits(centers, maximumDepth, maximumEntityCount, maximumRelationshipCount);
        return ExecuteReadAsync(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, snapshot);
                return GraphSnapshotProjection.Create(graph, generation).GetNeighborhood(
                    centers,
                    maximumDepth,
                    maximumEntityCount,
                    maximumRelationshipCount,
                    cancellationToken);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphRouteResult> FindRoutesAsync(
        GraphSnapshot snapshot,
        GraphEntityKey start,
        GraphEntityKey destination,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntityCount, 2);
        ValidateViewportLimits(maximumEntityCount, maximumRelationshipCount);
        return ExecuteReadAsync(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, snapshot);
                return GraphSnapshotProjection.Create(graph, generation).FindRoutes(
                    start,
                    destination,
                    maximumEntityCount,
                    maximumRelationshipCount,
                    cancellationToken);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphQuerySchema> GetSchemaAsync(
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(document, snapshot);
                GraphSnapshotProjection projection = GraphSnapshotProjection.Create(graph, generation);
                GraphQuerySchemaEntry[] nodeLabels = projection.Entities
                    .GroupBy(entity => entity.Key.TypeName, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new GraphQuerySchemaEntry(
                        group.Key,
                        group.LongCount(),
                        group.SelectMany(entity => entity.Value.LatestObservation?.Properties ?? [])
                            .Select(property => property.Name)))
                    .ToArray();
                GraphQuerySchemaEntry[] relationshipTypes = projection.Relationships
                    .GroupBy(relationship => relationship.Key.TypeName, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new GraphQuerySchemaEntry(
                        group.Key,
                        group.LongCount(),
                        group.SelectMany(relationship => relationship.Value.LatestObservation.Properties)
                            .Select(property => property.Name)))
                    .ToArray();
                return new GraphQuerySchema(snapshot, nodeLabels, relationshipTypes);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphQueryResult> ExecuteOpenCypherAsync(
        GraphQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteReadAsync(
            document =>
            {
                (GraphDocument graph, GraphGenerationDocument generation) = GetSnapshot(
                    document,
                    request.Snapshot);
                GraphSnapshotProjection projection = GraphSnapshotProjection.Create(graph, generation);
                return GraphCypherQueryEngine.Execute(
                    request,
                    (query, token) => JsonGraphCypherMatchSource.ReadMatches(
                        projection,
                        query,
                        token),
                    cancellationToken);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphStateSummary> ClearAsync(CancellationToken cancellationToken = default)
    {
        return ClearCoreAsync(null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphStateSummary> ClearAsync(
        GraphWriteTarget target,
        CancellationToken cancellationToken = default)
    {
        return ClearCoreAsync(target, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            writeGate.Dispose();
        }
    }

    private static GraphSnapshotDocument CreateDefaultSnapshot(DateTimeOffset utcNow)
    {
        GraphDocument graph = CreateGraphDocument(DefaultGraphName, string.Empty, utcNow);
        return new GraphSnapshotDocument
        {
            ActiveGraphId = graph.Id,
            Graphs = [graph],
        };
    }

    private static GraphDocument CreateGraphDocument(
        string name,
        string description,
        DateTimeOffset utcNow)
    {
        Guid graphId = Guid.NewGuid();
        Guid generationId = Guid.NewGuid();
        DateTimeOffset normalizedNow = utcNow.ToUniversalTime();
        return new GraphDocument
        {
            Id = graphId,
            Name = name,
            Description = description,
            CreatedAtUtc = normalizedNow,
            LastUpdatedAtUtc = normalizedNow,
            LastActivatedAtUtc = normalizedNow,
            ActiveGenerationId = generationId,
            Generations =
            [
                new GraphGenerationDocument
                {
                    Id = generationId,
                    CreatedAtUtc = normalizedNow,
                    LastUpdatedAtUtc = normalizedNow,
                },
            ],
        };
    }

    private static GraphSnapshotDocument DeserializeOrCreate(
        string? json,
        DateTimeOffset utcNow,
        out bool invalidSnapshot)
    {
        invalidSnapshot = false;
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateDefaultSnapshot(utcNow);
        }

        try
        {
            GraphSnapshotDocument document = GraphSnapshotJson.Deserialize(json);
            Validate(document);
            return document;
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or ArgumentException or FormatException or OverflowException)
        {
            invalidSnapshot = true;
            return CreateDefaultSnapshot(utcNow);
        }
    }

    private static void Validate(GraphSnapshotDocument document)
    {
        if (document.Graphs is null || document.Graphs.Count == 0)
        {
            throw new InvalidDataException("The graph collection is missing.");
        }

        HashSet<Guid> graphIds = [];
        HashSet<string> graphNames = new(StringComparer.Ordinal);
        foreach (GraphDocument graph in document.Graphs)
        {
            ValidateGraph(graph, graphIds, graphNames);
        }

        if (!graphIds.Contains(document.ActiveGraphId))
        {
            throw new InvalidDataException("The active graph does not exist.");
        }
    }

    private static void ValidateGraph(
        GraphDocument graph,
        HashSet<Guid> graphIds,
        HashSet<string> graphNames)
    {
        if (!graphIds.Add(graph.Id)
            || !graphNames.Add(GraphCatalogMetadata.NormalizeNameKey(graph.Name))
            || graph.Generations is null
            || graph.LabelOverrides is null
            || graph.Generations.Count == 0
            || graph.Generations.All(generation => generation.Id != graph.ActiveGenerationId))
        {
            throw new InvalidDataException("The graph snapshot is structurally invalid.");
        }

        HashSet<Guid> generationIds = [];
        foreach (GraphGenerationDocument generation in graph.Generations)
        {
            ValidateGeneration(graph, generation, generationIds);
        }
    }

    private static void ValidateGeneration(
        GraphDocument graph,
        GraphGenerationDocument generation,
        HashSet<Guid> generationIds)
    {
        if (!generationIds.Add(generation.Id) || generation.Ingestions is null)
        {
            throw new InvalidDataException("The graph generation is structurally invalid.");
        }

        foreach (GraphIngestionDocument ingestion in generation.Ingestions)
        {
            ValidateIngestion(ingestion);
        }

        EnforceStorageLimits(GraphSnapshotProjection.Create(graph, generation));
    }

    private static void ValidateIngestion(GraphIngestionDocument ingestion)
    {
        if (ingestion.Evidence is null
            || ingestion.EntityObservations is null
            || ingestion.RelationshipObservations is null)
        {
            throw new InvalidDataException("The graph ingestion is structurally invalid.");
        }

        _ = new GraphImportBatch(
            GraphSnapshotMapper.ToIngestion(ingestion),
            ingestion.Evidence.Select(GraphSnapshotMapper.ToEvidence),
            ingestion.EntityObservations.Select(GraphSnapshotMapper.ToObservation),
            ingestion.RelationshipObservations.Select(GraphSnapshotMapper.ToObservation));
    }

    private static GraphCatalog CreateCatalog(GraphSnapshotDocument document)
    {
        return new GraphCatalog(
            document.ActiveGraphId,
            document.Graphs
                .OrderByDescending(graph => graph.LastActivatedAtUtc)
                .ThenBy(graph => graph.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CreateCatalogEntry));
    }

    private static GraphCatalogEntry CreateCatalogEntry(GraphDocument graph)
    {
        GraphGenerationDocument generation = GetActiveGeneration(graph);
        GraphSnapshotProjection projection = GraphSnapshotProjection.Create(graph, generation);
        return new GraphCatalogEntry(
            graph.Id,
            graph.Name,
            graph.Description,
            generation.Id,
            graph.CreatedAtUtc,
            graph.LastUpdatedAtUtc,
            graph.LastActivatedAtUtc,
            projection.Entities.Count,
            projection.Relationships.Count);
    }

    private static GraphStateSummary CreateState(
        GraphDocument graph,
        GraphGenerationDocument generation)
    {
        GraphSnapshotProjection projection = GraphSnapshotProjection.Create(graph, generation);
        return new GraphStateSummary(
            graph.Id,
            graph.Name,
            graph.Description,
            generation.Id,
            generation.CreatedAtUtc,
            generation.LastUpdatedAtUtc,
            projection.Entities.Count,
            projection.Relationships.Count,
            generation.Ingestions.Count,
            generation.Ingestions.Sum(ingestion => (long)ingestion.Evidence.Count));
    }

    private static IEnumerable<GraphTimelinePoint> CreateTimeline(
        GraphDocument graph,
        GraphGenerationDocument generation)
    {
        yield return new GraphTimelinePoint(
            new GraphSnapshot(graph.Id, generation.Id),
            generation.CreatedAtUtc,
            null,
            null,
            "Generation started");

        foreach (GraphIngestionDocument ingestion in generation.Ingestions)
        {
            yield return new GraphTimelinePoint(
                new GraphSnapshot(graph.Id, generation.Id),
                ingestion.EntityObservations
                    .Select(observation => observation.TemporalInterval.DiscoveredAtUtc)
                    .Concat(ingestion.RelationshipObservations.Select(
                        observation => observation.TemporalInterval.DiscoveredAtUtc))
                    .Append(ingestion.CompletedAtUtc)
                    .Max(),
                ingestion.Id,
                ingestion.SourceKind,
                ingestion.SourceName);
        }
    }

    private static DateTimeOffset LaterOf(DateTimeOffset first, DateTimeOffset second)
        => first >= second ? first : second;

    private static GraphDocument GetActiveGraph(GraphSnapshotDocument document)
        => GetGraph(document, document.ActiveGraphId);

    private static GraphDocument GetGraph(GraphSnapshotDocument document, Guid graphId)
    {
        return document.Graphs.FirstOrDefault(graph => graph.Id == graphId)
            ?? throw new InvalidOperationException("The graph does not exist.");
    }

    private static GraphGenerationDocument GetActiveGeneration(GraphDocument graph)
    {
        return graph.Generations.FirstOrDefault(generation => generation.Id == graph.ActiveGenerationId)
            ?? throw new InvalidOperationException("The active graph generation does not exist.");
    }

    private static (GraphDocument Graph, GraphGenerationDocument Generation) GetSnapshot(
        GraphSnapshotDocument document,
        GraphSnapshot graphSnapshot)
    {
        GraphDocument graph = GetGraph(document, graphSnapshot.GraphId);
        GraphGenerationDocument generation = graph.Generations.FirstOrDefault(
            candidate => candidate.Id == graphSnapshot.GenerationId)
            ?? throw new InvalidOperationException("The graph generation no longer exists.");
        return (graph, generation);
    }

    private static GraphEntityKeyDocument ToDocument(GraphEntityKey entity)
    {
        return new GraphEntityKeyDocument
        {
            Kind = entity.Kind,
            TypeName = entity.TypeName,
            CanonicalId = entity.CanonicalId,
            SourceNamespace = entity.SourceNamespace,
        };
    }

    private static void ValidateWriteTarget(GraphDocument graph, GraphWriteTarget target)
    {
        if (graph.ActiveGenerationId != target.ExpectedGenerationId)
        {
            throw new InvalidOperationException("The graph changed before the operation could be completed.");
        }
    }

    private static void ValidateSearch(string searchText, int maximumResults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumResults,
            GraphStorageLimits.MaximumSearchResults);
    }

    private static void ValidateNeighborhoodLimits(
        IReadOnlyCollection<GraphEntityKey> centers,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount)
    {
        ArgumentNullException.ThrowIfNull(centers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumDepth,
            GraphStorageLimits.MaximumNeighborhoodDepth);
        ValidateViewportLimits(maximumEntityCount, maximumRelationshipCount);
    }

    private static void ValidatePositiveViewportLimits(
        int maximumEntityCount,
        int maximumRelationshipCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelationshipCount);
    }

    private static void ValidateViewportLimits(int maximumEntityCount, int maximumRelationshipCount)
    {
        ValidatePositiveViewportLimits(maximumEntityCount, maximumRelationshipCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumEntityCount,
            GraphStorageLimits.MaximumViewportEntityCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumRelationshipCount,
            GraphStorageLimits.MaximumViewportRelationshipCount);
    }

    private static void ValidateEvidencePayloads(
        GraphSnapshotDocument document,
        IReadOnlyCollection<GraphEvidenceDocument> importedEvidence)
    {
        Dictionary<string, (string SchemaJson, string RowJson)> existingPayloads = document.Graphs
            .SelectMany(graph => graph.Generations)
            .SelectMany(generation => generation.Ingestions)
            .SelectMany(ingestion => ingestion.Evidence)
            .GroupBy(evidence => evidence.ContentHash, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (group.First().SchemaJson, group.First().RowJson),
                StringComparer.Ordinal);
        foreach (GraphEvidenceDocument evidence in importedEvidence)
        {
            if (existingPayloads.TryGetValue(
                evidence.ContentHash,
                out (string SchemaJson, string RowJson) existing)
                && (!string.Equals(existing.SchemaJson, evidence.SchemaJson, StringComparison.Ordinal)
                    || !string.Equals(existing.RowJson, evidence.RowJson, StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"Evidence hash '{evidence.ContentHash}' refers to different payloads.");
            }
        }
    }

    private static void EnforceStorageLimits(GraphSnapshotProjection projection)
    {
        if (projection.Entities.Count > GraphStorageLimits.MaximumEntityCount)
        {
            throw new InvalidOperationException(
                $"A graph generation cannot contain more than {GraphStorageLimits.MaximumEntityCount:N0} entities.");
        }

        if (projection.Relationships.Count > GraphStorageLimits.MaximumRelationshipCount)
        {
            throw new InvalidOperationException(
                "A graph generation cannot contain more than "
                + $"{GraphStorageLimits.MaximumRelationshipCount:N0} relationships.");
        }
    }

    private async Task<GraphImportResult> ImportCoreAsync(
        GraphWriteTarget? requestedTarget,
        IGraphImportSource source,
        GraphImportMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        GraphIngestionDocument ingestion = GraphSnapshotMapper.ToDocument(source);
        return await ExecuteWriteAsync(
            document =>
            {
                GraphDocument graph = requestedTarget is GraphWriteTarget target
                    ? GetGraph(document, target.GraphId)
                    : GetActiveGraph(document);
                if (requestedTarget is GraphWriteTarget pinnedTarget)
                {
                    ValidateWriteTarget(graph, pinnedTarget);
                }

                if (document.Graphs
                    .SelectMany(candidate => candidate.Generations)
                    .SelectMany(generation => generation.Ingestions)
                    .Any(existing => existing.Id == ingestion.Id))
                {
                    throw new InvalidOperationException("The graph ingestion already exists.");
                }

                ValidateEvidencePayloads(document, ingestion.Evidence);
                GraphGenerationDocument previousGeneration = GetActiveGeneration(graph);
                GraphSnapshotProjection previousProjection = mode == GraphImportMode.Add
                    ? GraphSnapshotProjection.Create(graph, previousGeneration)
                    : GraphSnapshotProjection.Create(
                        graph,
                        new GraphGenerationDocument
                        {
                            Id = Guid.NewGuid(),
                            CreatedAtUtc = ingestion.StartedAtUtc,
                            LastUpdatedAtUtc = ingestion.StartedAtUtc,
                        });
                GraphGenerationDocument generation = previousGeneration;
                if (mode == GraphImportMode.Replace)
                {
                    generation = new GraphGenerationDocument
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = ingestion.StartedAtUtc,
                        LastUpdatedAtUtc = ingestion.StartedAtUtc,
                    };
                    graph.Generations.Add(generation);
                    graph.ActiveGenerationId = generation.Id;
                }

                generation.Ingestions.Add(ingestion);
                generation.LastUpdatedAtUtc = LaterOf(generation.CreatedAtUtc, ingestion.CompletedAtUtc);
                graph.LastUpdatedAtUtc = LaterOf(graph.LastUpdatedAtUtc, ingestion.CompletedAtUtc);
                GraphSnapshotProjection projection = GraphSnapshotProjection.Create(graph, generation);
                EnforceStorageLimits(projection);
                return new GraphImportResult(
                    graph.Id,
                    generation.Id,
                    ingestion.Id,
                    ingestion.Evidence.Count,
                    projection.Entities.Keys.Count(entity => !previousProjection.Entities.ContainsKey(entity)),
                    ingestion.EntityObservations.Count,
                    projection.Relationships.Keys.Count(
                        relationship => !previousProjection.Relationships.ContainsKey(relationship)),
                    ingestion.RelationshipObservations.Count);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private Task<GraphStateSummary> ClearCoreAsync(
        GraphWriteTarget? requestedTarget,
        CancellationToken cancellationToken)
    {
        return ExecuteWriteAsync(
            document =>
            {
                GraphDocument graph = requestedTarget is GraphWriteTarget target
                    ? GetGraph(document, target.GraphId)
                    : GetActiveGraph(document);
                if (requestedTarget is GraphWriteTarget pinnedTarget)
                {
                    ValidateWriteTarget(graph, pinnedTarget);
                }

                DateTimeOffset utcNow = timeProvider.GetUtcNow();
                GraphGenerationDocument generation = new()
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = utcNow,
                    LastUpdatedAtUtc = utcNow,
                };
                graph.Generations.Add(generation);
                graph.ActiveGenerationId = generation.Id;
                graph.LastUpdatedAtUtc = LaterOf(graph.LastUpdatedAtUtc, utcNow);
                return CreateState(graph, generation);
            },
            cancellationToken);
    }

    private async Task<TResult> ExecuteWriteAsync<TResult>(
        Func<GraphSnapshotDocument, TResult> action,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string originalJson = GraphSnapshotJson.Serialize(snapshot);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            TResult result = action(snapshot);
            string updatedJson = GraphSnapshotJson.Serialize(snapshot);
            await snapshotStore.SaveAsync(updatedJson, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            snapshot = GraphSnapshotJson.Deserialize(originalJson);
            throw;
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task<TResult> ExecuteReadAsync<TResult>(
        Func<GraphSnapshotDocument, TResult> action,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action(snapshot);
        }
        finally
        {
            writeGate.Release();
        }
    }
}

#pragma warning restore SA1600
