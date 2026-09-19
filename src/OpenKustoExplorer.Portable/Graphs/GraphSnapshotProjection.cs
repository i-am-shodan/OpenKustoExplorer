using OpenKustoExplorer.Graph;

#pragma warning disable SA1201, SA1600

namespace OpenKustoExplorer.Portable.Graphs;

internal sealed class GraphSnapshotProjection
{
    private const int MaximumEvidenceRecords = 25;
    private readonly GraphDocument graph;
    private readonly Dictionary<GraphEntityKey, MaterializedEntity> entities = [];
    private readonly Dictionary<GraphRelationshipKey, MaterializedRelationship> relationships = [];

    private GraphSnapshotProjection(GraphDocument graph)
    {
        this.graph = graph;
    }

    internal IReadOnlyDictionary<GraphEntityKey, MaterializedEntity> Entities => entities;

    internal IReadOnlyDictionary<GraphRelationshipKey, MaterializedRelationship> Relationships => relationships;

    internal static GraphSnapshotProjection Create(
        GraphDocument graph,
        GraphGenerationDocument generation,
        Guid? lastIngestionId = null)
    {
        GraphSnapshotProjection projection = new(graph);
        foreach (GraphIngestionDocument ingestion in generation.Ingestions)
        {
            projection.Add(ingestion);
            if (lastIngestionId == ingestion.Id)
            {
                break;
            }
        }

        if (lastIngestionId is not null
            && generation.Ingestions.All(ingestion => ingestion.Id != lastIngestionId))
        {
            throw new InvalidOperationException("The graph timeline point no longer exists.");
        }

        return projection;
    }

    internal GraphEntityDetails? GetEntityDetails(GraphEntityKey entity)
    {
        if (!entities.TryGetValue(entity, out MaterializedEntity? materialized))
        {
            return null;
        }

        GraphEntityObservationDocument? latest = materialized.LatestObservation;
        return new GraphEntityDetails(
            CreateSummary(entity),
            latest?.SourceLabels ?? [],
            CreateProperties(latest?.Properties),
            materialized.Observations.Count,
            materialized.Evidence.Count,
            materialized.Evidence
                .OrderByDescending(record => record.DiscoveredAtUtc)
                .ThenByDescending(record => record.IngestionId)
                .Take(MaximumEvidenceRecords));
    }

    internal GraphRelationshipDetails? GetRelationshipDetails(GraphRelationshipKey relationship)
    {
        if (!relationships.TryGetValue(relationship, out MaterializedRelationship? materialized))
        {
            return null;
        }

        GraphRelationshipObservationDocument latest = materialized.LatestObservation;
        return new GraphRelationshipDetails(
            relationship,
            latest.SourceLabels,
            CreateProperties(latest.Properties),
            materialized.FirstDiscoveredAtUtc,
            materialized.LastUpdatedAtUtc,
            materialized.Observations.Count,
            materialized.Evidence.Count,
            materialized.Evidence
                .OrderByDescending(record => record.DiscoveredAtUtc)
                .ThenByDescending(record => record.IngestionId)
                .Take(MaximumEvidenceRecords));
    }

    internal IReadOnlyList<GraphEntitySummary> Search(string searchText, int maximumResults)
    {
        string normalizedSearch = searchText.Trim();
        return entities.Keys
            .Select(CreateSummary)
            .Where(summary => Matches(summary, normalizedSearch))
            .OrderBy(summary => SearchRank(summary, normalizedSearch))
            .ThenByDescending(summary => summary.Degree)
            .ThenByDescending(summary => summary.LastUpdatedAtUtc)
            .ThenBy(summary => summary.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .Take(maximumResults)
            .ToArray();
    }

    internal GraphViewport GetViewport(
        GraphEntityKey? requestedCenter,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        if (entities.Count == 0)
        {
            return new GraphViewport(null, [], [], false);
        }

        GraphEntityKey? center = requestedCenter;
        if (center is null)
        {
            center = entities.Keys
                .Select(CreateSummary)
                .OrderByDescending(summary => summary.Degree)
                .ThenByDescending(summary => summary.LastUpdatedAtUtc)
                .ThenBy(summary => summary.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .Select(summary => (GraphEntityKey?)summary.Entity)
                .First();
        }
        else if (!entities.ContainsKey(center.Value))
        {
            return new GraphViewport(null, [], [], false);
        }

        if (requestedCenter is null)
        {
            GraphEntityKey[] selected = entities.Keys
                .Select(CreateSummary)
                .OrderByDescending(summary => summary.Degree)
                .ThenByDescending(summary => summary.LastUpdatedAtUtc)
                .ThenBy(summary => summary.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .Take(maximumEntityCount)
                .Select(summary => summary.Entity)
                .ToArray();
            return CreateViewport(
                center,
                selected,
                maximumEntityCount,
                maximumRelationshipCount,
                entities.Count > selected.Length,
                cancellationToken);
        }

        GraphEntityKey centerEntity = center
            ?? throw new InvalidOperationException("A non-empty graph must have a viewport center.");
        HashSet<GraphEntityKey> selectedEntities = [centerEntity];
        List<GraphRelationshipKey> selectedRelationships = [];
        bool isTruncated = false;
        foreach (GraphRelationshipKey relationship in OrderedRelationships())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (relationship.Source != centerEntity && relationship.Target != centerEntity)
            {
                continue;
            }

            GraphEntityKey neighbor = relationship.Source == centerEntity
                ? relationship.Target
                : relationship.Source;
            if (!selectedEntities.Contains(neighbor) && selectedEntities.Count >= maximumEntityCount)
            {
                isTruncated = true;
                continue;
            }

            if (selectedRelationships.Count >= maximumRelationshipCount)
            {
                isTruncated = true;
                continue;
            }

            selectedEntities.Add(neighbor);
            selectedRelationships.Add(relationship);
        }

        return new GraphViewport(
            centerEntity,
            selectedEntities.Select(CreateSummary),
            selectedRelationships,
            isTruncated);
    }

    internal GraphViewport GetNeighborhood(
        IReadOnlyCollection<GraphEntityKey> requestedCenters,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        GraphEntityKey[] centers = requestedCenters
            .Distinct()
            .Where(entities.ContainsKey)
            .ToArray();
        if (centers.Length == 0)
        {
            return new GraphViewport(null, [], [], false);
        }

        HashSet<GraphEntityKey> selected = [];
        Queue<(GraphEntityKey Entity, int Depth)> frontier = new();
        bool isTruncated = false;
        foreach (GraphEntityKey center in centers)
        {
            if (selected.Count >= maximumEntityCount)
            {
                isTruncated = true;
                break;
            }

            if (selected.Add(center))
            {
                frontier.Enqueue((center, 0));
            }
        }

        Dictionary<GraphEntityKey, List<(GraphEntityKey Neighbor, GraphRelationshipKey Relationship)>> adjacency =
            CreateAdjacency(cancellationToken);
        while (frontier.TryDequeue(out (GraphEntityKey Entity, int Depth) current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Depth >= maximumDepth
                || !adjacency.TryGetValue(
                    current.Entity,
                    out List<(GraphEntityKey Neighbor, GraphRelationshipKey Relationship)>? neighbors))
            {
                continue;
            }

            foreach ((GraphEntityKey neighbor, _) in neighbors)
            {
                if (selected.Contains(neighbor))
                {
                    continue;
                }

                if (selected.Count >= maximumEntityCount)
                {
                    isTruncated = true;
                    continue;
                }

                selected.Add(neighbor);
                frontier.Enqueue((neighbor, current.Depth + 1));
            }
        }

        return CreateViewport(
            centers[0],
            selected,
            maximumEntityCount,
            maximumRelationshipCount,
            isTruncated,
            cancellationToken);
    }

    internal GraphRouteResult FindRoutes(
        GraphEntityKey start,
        GraphEntityKey destination,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken)
    {
        if (!entities.ContainsKey(start))
        {
            throw new InvalidOperationException("The route start does not exist in the selected graph.");
        }

        if (!entities.ContainsKey(destination))
        {
            throw new InvalidOperationException("The route destination does not exist in the selected graph.");
        }

        if (start == destination)
        {
            return new GraphRouteResult(
                start,
                destination,
                0,
                new GraphViewport(start, [CreateSummary(start)], [], false));
        }

        Dictionary<GraphEntityKey, List<(GraphEntityKey Neighbor, GraphRelationshipKey Relationship)>> adjacency =
            CreateAdjacency(cancellationToken);
        Dictionary<GraphEntityKey, int> fromStart = FindDistances(adjacency, start, cancellationToken);
        if (!fromStart.TryGetValue(destination, out int shortestHopCount))
        {
            return new GraphRouteResult(
                start,
                destination,
                null,
                new GraphViewport(start, [CreateSummary(start), CreateSummary(destination)], [], false));
        }

        Dictionary<GraphEntityKey, int> fromDestination = FindDistances(adjacency, destination, cancellationToken);
        HashSet<GraphRelationshipKey> routeRelationships = [];
        foreach (GraphRelationshipKey relationship in OrderedRelationships())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsOnShortestRoute(relationship.Source, relationship.Target)
                || IsOnShortestRoute(relationship.Target, relationship.Source))
            {
                routeRelationships.Add(relationship);
            }
        }

        HashSet<GraphEntityKey> routeEntities = [start, destination];
        foreach (GraphRelationshipKey relationship in routeRelationships)
        {
            routeEntities.Add(relationship.Source);
            routeEntities.Add(relationship.Target);
        }

        bool isTruncated = routeEntities.Count > maximumEntityCount
            || routeRelationships.Count > maximumRelationshipCount;
        if (isTruncated)
        {
            (routeEntities, routeRelationships) = FindOneShortestRoute(
                adjacency,
                fromStart,
                start,
                destination,
                cancellationToken);
            if (routeEntities.Count > maximumEntityCount || routeRelationships.Count > maximumRelationshipCount)
            {
                throw new InvalidOperationException(
                    $"The shortest route has {shortestHopCount} hops and exceeds the graph viewport limits.");
            }
        }

        GraphViewport viewport = new(
            start,
            routeEntities.Select(CreateSummary),
            OrderedRelationships().Where(routeRelationships.Contains),
            isTruncated);
        return new GraphRouteResult(start, destination, shortestHopCount, viewport);

        bool IsOnShortestRoute(GraphEntityKey source, GraphEntityKey target)
        {
            return fromStart.TryGetValue(source, out int sourceDistance)
                && fromDestination.TryGetValue(target, out int targetDistance)
                && sourceDistance + 1 + targetDistance == shortestHopCount;
        }
    }

    internal GraphEntitySummary CreateSummary(GraphEntityKey entity)
    {
        MaterializedEntity materialized = entities[entity];
        string displayLabel = graph.LabelOverrides
            .LastOrDefault(candidate => GraphSnapshotMapper.ToEntityKey(candidate.Entity) == entity)
            ?.DisplayLabel
            ?? materialized.LatestObservation?.DisplayLabel
            ?? entity.CanonicalId;
        long degree = relationships.Keys.Count(
            relationship => relationship.Source == entity || relationship.Target == entity);
        return new GraphEntitySummary(
            entity,
            displayLabel,
            materialized.FirstDiscoveredAtUtc,
            materialized.LastUpdatedAtUtc,
            degree);
    }

    private static GraphEntityProperty[] CreateProperties(
        IReadOnlyList<GraphPropertyDocument>? properties)
    {
        return properties?
            .Select(property => new GraphEntityProperty(property.Name, property.Value))
            .ToArray()
            ?? [];
    }

    private static bool Matches(GraphEntitySummary summary, string searchText)
    {
        return summary.DisplayLabel.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || summary.Entity.CanonicalId.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || summary.Entity.TypeName.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static int SearchRank(GraphEntitySummary summary, string searchText)
    {
        if (string.Equals(summary.DisplayLabel, searchText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(summary.Entity.CanonicalId, searchText, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (summary.DisplayLabel.StartsWith(searchText, StringComparison.OrdinalIgnoreCase)
            || summary.Entity.CanonicalId.StartsWith(searchText, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static Dictionary<GraphEntityKey, int> FindDistances(
        Dictionary<GraphEntityKey, List<(GraphEntityKey Neighbor, GraphRelationshipKey Relationship)>> adjacency,
        GraphEntityKey origin,
        CancellationToken cancellationToken)
    {
        Dictionary<GraphEntityKey, int> distances = new() { [origin] = 0 };
        Queue<GraphEntityKey> frontier = new();
        frontier.Enqueue(origin);
        while (frontier.TryDequeue(out GraphEntityKey current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!adjacency.TryGetValue(
                current,
                out List<(GraphEntityKey Neighbor, GraphRelationshipKey Relationship)>? neighbors))
            {
                continue;
            }

            foreach ((GraphEntityKey neighbor, _) in neighbors)
            {
                if (distances.TryAdd(neighbor, distances[current] + 1))
                {
                    frontier.Enqueue(neighbor);
                }
            }
        }

        return distances;
    }

    private static (HashSet<GraphEntityKey> Entities, HashSet<GraphRelationshipKey> Relationships)
        FindOneShortestRoute(
            Dictionary<GraphEntityKey, List<(GraphEntityKey Neighbor, GraphRelationshipKey Relationship)>>
                adjacency,
            Dictionary<GraphEntityKey, int> distances,
            GraphEntityKey start,
            GraphEntityKey destination,
            CancellationToken cancellationToken)
    {
        HashSet<GraphEntityKey> routeEntities = [destination];
        HashSet<GraphRelationshipKey> routeRelationships = [];
        GraphEntityKey current = destination;
        while (current != start)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int expectedDistance = distances[current] - 1;
            (GraphEntityKey Neighbor, GraphRelationshipKey Relationship) predecessor = adjacency[current]
                .Where(candidate => distances.GetValueOrDefault(candidate.Neighbor, -1) == expectedDistance)
                .OrderBy(candidate => candidate.Neighbor.ToString(), StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Relationship.ToString(), StringComparer.Ordinal)
                .First();
            routeRelationships.Add(predecessor.Relationship);
            routeEntities.Add(predecessor.Neighbor);
            current = predecessor.Neighbor;
        }

        return (routeEntities, routeRelationships);
    }

    private void Add(GraphIngestionDocument ingestion)
    {
        Dictionary<string, GraphEvidenceDocument> evidenceById = ingestion.Evidence.ToDictionary(
            evidence => evidence.OccurrenceId,
            StringComparer.Ordinal);
        foreach (GraphEntityObservationDocument observation in ingestion.EntityObservations)
        {
            GraphEntityKey entity = GraphSnapshotMapper.ToEntityKey(observation.Entity);
            if (!entities.TryGetValue(entity, out MaterializedEntity? materialized))
            {
                materialized = new MaterializedEntity(observation.TemporalInterval.DiscoveredAtUtc);
                entities.Add(entity, materialized);
            }

            materialized.Add(observation, ingestion, evidenceById);
        }

        foreach (GraphRelationshipObservationDocument observation in ingestion.RelationshipObservations)
        {
            GraphRelationshipKey relationship = GraphSnapshotMapper.ToRelationshipKey(observation.Relationship);
            EnsureImplicitEntity(relationship.Source, observation.TemporalInterval.DiscoveredAtUtc);
            EnsureImplicitEntity(relationship.Target, observation.TemporalInterval.DiscoveredAtUtc);
            if (!relationships.TryGetValue(relationship, out MaterializedRelationship? materialized))
            {
                materialized = new MaterializedRelationship(observation.TemporalInterval.DiscoveredAtUtc);
                relationships.Add(relationship, materialized);
            }

            materialized.Add(observation, ingestion, evidenceById);
        }
    }

    private void EnsureImplicitEntity(GraphEntityKey entity, DateTimeOffset discoveredAtUtc)
    {
        if (!entities.ContainsKey(entity))
        {
            entities.Add(entity, new MaterializedEntity(discoveredAtUtc));
        }
    }

    private GraphViewport CreateViewport(
        GraphEntityKey? center,
        IEnumerable<GraphEntityKey> requestedEntities,
        int maximumEntityCount,
        int maximumRelationshipCount,
        bool isAlreadyTruncated,
        CancellationToken cancellationToken)
    {
        GraphEntityKey[] selected = requestedEntities.Take(maximumEntityCount).ToArray();
        HashSet<GraphEntityKey> selectedSet = selected.ToHashSet();
        List<GraphRelationshipKey> selectedRelationships = [];
        bool isTruncated = isAlreadyTruncated;
        foreach (GraphRelationshipKey relationship in OrderedRelationships())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!selectedSet.Contains(relationship.Source) || !selectedSet.Contains(relationship.Target))
            {
                continue;
            }

            if (selectedRelationships.Count >= maximumRelationshipCount)
            {
                isTruncated = true;
                continue;
            }

            selectedRelationships.Add(relationship);
        }

        return new GraphViewport(
            center is GraphEntityKey centerEntity && selectedSet.Contains(centerEntity) ? center : null,
            selected.Select(CreateSummary),
            selectedRelationships,
            isTruncated);
    }

    private Dictionary<GraphEntityKey, List<(GraphEntityKey Neighbor, GraphRelationshipKey Relationship)>>
        CreateAdjacency(CancellationToken cancellationToken)
    {
        Dictionary<GraphEntityKey, List<(GraphEntityKey Neighbor, GraphRelationshipKey Relationship)>> adjacency = [];
        foreach (GraphRelationshipKey relationship in OrderedRelationships())
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddNeighbor(relationship.Source, relationship.Target, relationship);
            AddNeighbor(relationship.Target, relationship.Source, relationship);
        }

        return adjacency;

        void AddNeighbor(
            GraphEntityKey entity,
            GraphEntityKey neighbor,
            GraphRelationshipKey relationship)
        {
            if (!adjacency.TryGetValue(
                entity,
                out List<(GraphEntityKey Neighbor, GraphRelationshipKey Relationship)>? neighbors))
            {
                neighbors = [];
                adjacency.Add(entity, neighbors);
            }

            neighbors.Add((neighbor, relationship));
        }
    }

    private IEnumerable<GraphRelationshipKey> OrderedRelationships()
    {
        return relationships.Keys
            .OrderBy(relationship => relationship.TypeName, StringComparer.Ordinal)
            .ThenBy(relationship => relationship.Source.ToString(), StringComparer.Ordinal)
            .ThenBy(relationship => relationship.Target.ToString(), StringComparer.Ordinal)
            .ThenBy(relationship => relationship.Discriminator, StringComparer.Ordinal);
    }

    internal sealed class MaterializedEntity
    {
        private readonly HashSet<(Guid IngestionId, string OccurrenceId)> evidenceIds = [];

        internal MaterializedEntity(DateTimeOffset discoveredAtUtc)
        {
            FirstDiscoveredAtUtc = discoveredAtUtc;
            LastUpdatedAtUtc = discoveredAtUtc;
        }

        internal List<GraphEvidenceRecord> Evidence { get; } = [];

        internal DateTimeOffset FirstDiscoveredAtUtc { get; private set; }

        internal GraphEntityObservationDocument? LatestObservation { get; private set; }

        internal DateTimeOffset LastUpdatedAtUtc { get; private set; }

        internal List<GraphEntityObservationDocument> Observations { get; } = [];

        internal void Add(
            GraphEntityObservationDocument observation,
            GraphIngestionDocument ingestion,
            IReadOnlyDictionary<string, GraphEvidenceDocument> evidenceById)
        {
            Observations.Add(observation);
            DateTimeOffset discoveredAtUtc = observation.TemporalInterval.DiscoveredAtUtc;
            if (LatestObservation is null || discoveredAtUtc >= LastUpdatedAtUtc)
            {
                LatestObservation = observation;
                LastUpdatedAtUtc = discoveredAtUtc;
            }

            if (discoveredAtUtc < FirstDiscoveredAtUtc)
            {
                FirstDiscoveredAtUtc = discoveredAtUtc;
            }

            AddEvidence(Evidence, evidenceIds, observation.EvidenceIds, ingestion, evidenceById, discoveredAtUtc);
        }
    }

    internal sealed class MaterializedRelationship
    {
        private readonly HashSet<(Guid IngestionId, string OccurrenceId)> evidenceIds = [];

        internal MaterializedRelationship(DateTimeOffset discoveredAtUtc)
        {
            FirstDiscoveredAtUtc = discoveredAtUtc;
            LastUpdatedAtUtc = discoveredAtUtc;
        }

        internal List<GraphEvidenceRecord> Evidence { get; } = [];

        internal DateTimeOffset FirstDiscoveredAtUtc { get; private set; }

        internal GraphRelationshipObservationDocument LatestObservation { get; private set; } = new();

        internal DateTimeOffset LastUpdatedAtUtc { get; private set; }

        internal List<GraphRelationshipObservationDocument> Observations { get; } = [];

        internal void Add(
            GraphRelationshipObservationDocument observation,
            GraphIngestionDocument ingestion,
            IReadOnlyDictionary<string, GraphEvidenceDocument> evidenceById)
        {
            Observations.Add(observation);
            DateTimeOffset discoveredAtUtc = observation.TemporalInterval.DiscoveredAtUtc;
            if (Observations.Count == 1 || discoveredAtUtc >= LastUpdatedAtUtc)
            {
                LatestObservation = observation;
                LastUpdatedAtUtc = discoveredAtUtc;
            }

            if (discoveredAtUtc < FirstDiscoveredAtUtc)
            {
                FirstDiscoveredAtUtc = discoveredAtUtc;
            }

            AddEvidence(Evidence, evidenceIds, observation.EvidenceIds, ingestion, evidenceById, discoveredAtUtc);
        }
    }

    private static void AddEvidence(
        ICollection<GraphEvidenceRecord> destination,
        ISet<(Guid IngestionId, string OccurrenceId)> retainedIds,
        IEnumerable<string> evidenceIds,
        GraphIngestionDocument ingestion,
        IReadOnlyDictionary<string, GraphEvidenceDocument> evidenceById,
        DateTimeOffset discoveredAtUtc)
    {
        foreach (string evidenceId in evidenceIds)
        {
            if (!evidenceById.TryGetValue(evidenceId, out GraphEvidenceDocument? evidence)
                || !retainedIds.Add((ingestion.Id, evidenceId)))
            {
                continue;
            }

            destination.Add(new GraphEvidenceRecord(
                ingestion.Id,
                ingestion.SourceKind,
                ingestion.SourceId,
                ingestion.SourceName,
                new Uri(ingestion.ClusterUri),
                ingestion.DatabaseName,
                ingestion.QueryText,
                ingestion.CompletedAtUtc,
                discoveredAtUtc,
                evidence.TableName,
                evidence.RowOrdinal,
                evidence.SchemaJson,
                evidence.RowJson));
        }
    }
}

#pragma warning restore SA1201, SA1600
