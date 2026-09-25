using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using BoundNode = OpenKustoExplorer.Graph.Query.GraphCypherQueryEngine.BoundNode;
using BoundRelationship = OpenKustoExplorer.Graph.Query.GraphCypherQueryEngine.BoundRelationship;
using MatchRow = OpenKustoExplorer.Graph.Query.GraphCypherQueryEngine.MatchRow;

namespace OpenKustoExplorer.Portable.Graphs;

/// <summary>
/// Enumerates normalized structural matches from an in-memory graph snapshot.
/// </summary>
internal static class JsonGraphCypherMatchSource
{
    /// <summary>
    /// Reads node or one-hop relationship bindings for the shared query engine.
    /// </summary>
    /// <param name="projection">The materialized graph snapshot.</param>
    /// <param name="query">The parsed query plan.</param>
    /// <param name="cancellationToken">A token that cancels match enumeration.</param>
    /// <returns>The structural matches in snapshot order.</returns>
    internal static IEnumerable<MatchRow> ReadMatches(
        GraphSnapshotProjection projection,
        ParsedQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(query);
        return ReadMatchesCore(projection, query, cancellationToken);
    }

    private static IEnumerable<MatchRow> ReadMatchesCore(
        GraphSnapshotProjection projection,
        ParsedQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Relationship is null)
        {
            foreach (GraphEntityKey entity in OrderEntities(projection.Entities.Keys))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new MatchRow(query, [CreateNode(projection, entity)], null);
            }

            yield break;
        }

        List<RelationshipBinding> bindings = [];
        foreach (GraphRelationshipKey relationship in projection.Relationships.Keys)
        {
            if (query.Relationship.Direction == RelationshipDirection.Incoming)
            {
                bindings.Add(new RelationshipBinding(
                    relationship.Target,
                    relationship.Source,
                    relationship));
                continue;
            }

            bindings.Add(new RelationshipBinding(
                relationship.Source,
                relationship.Target,
                relationship));

            if (query.Relationship.Direction == RelationshipDirection.Undirected
                && relationship.Source != relationship.Target)
            {
                bindings.Add(new RelationshipBinding(
                    relationship.Target,
                    relationship.Source,
                    relationship));
            }
        }

        foreach (RelationshipBinding binding in OrderBindings(bindings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateRelationshipMatch(
                projection,
                query,
                binding.First,
                binding.Second,
                CreateRelationship(projection, binding.Relationship));
        }
    }

    private static IOrderedEnumerable<GraphEntityKey> OrderEntities(IEnumerable<GraphEntityKey> entities)
    {
        return entities
            .OrderBy(entity => entity.Kind)
            .ThenBy(entity => entity.TypeName, StringComparer.Ordinal)
            .ThenBy(entity => entity.CanonicalId, StringComparer.Ordinal)
            .ThenBy(entity => entity.SourceNamespace, StringComparer.Ordinal);
    }

    private static IOrderedEnumerable<RelationshipBinding> OrderBindings(
        IEnumerable<RelationshipBinding> bindings)
    {
        return bindings
            .OrderBy(binding => binding.First.Kind)
            .ThenBy(binding => binding.First.TypeName, StringComparer.Ordinal)
            .ThenBy(binding => binding.First.CanonicalId, StringComparer.Ordinal)
            .ThenBy(binding => binding.First.SourceNamespace, StringComparer.Ordinal)
            .ThenBy(binding => binding.Second.Kind)
            .ThenBy(binding => binding.Second.TypeName, StringComparer.Ordinal)
            .ThenBy(binding => binding.Second.CanonicalId, StringComparer.Ordinal)
            .ThenBy(binding => binding.Second.SourceNamespace, StringComparer.Ordinal)
            .ThenBy(binding => binding.Relationship.TypeName, StringComparer.Ordinal)
            .ThenBy(binding => binding.Relationship.Discriminator, StringComparer.Ordinal);
    }

    private static MatchRow CreateRelationshipMatch(
        GraphSnapshotProjection projection,
        ParsedQuery query,
        GraphEntityKey first,
        GraphEntityKey second,
        BoundRelationship relationship)
    {
        return new MatchRow(
            query,
            [CreateNode(projection, first), CreateNode(projection, second)],
            relationship);
    }

    private static BoundNode CreateNode(
        GraphSnapshotProjection projection,
        GraphEntityKey entity)
    {
        GraphSnapshotProjection.MaterializedEntity materialized = projection.Entities[entity];
        GraphEntityObservationDocument? latest = materialized.LatestObservation;
        IReadOnlyDictionary<string, string> properties = CreateProperties(latest?.Properties);
        return new BoundNode(
            projection.CreateSummary(entity),
            properties,
            latest?.SourceLabels ?? [],
            GraphCypherQueryEngine.WriteProperties(properties));
    }

    private static BoundRelationship CreateRelationship(
        GraphSnapshotProjection projection,
        GraphRelationshipKey relationship)
    {
        GraphSnapshotProjection.MaterializedRelationship materialized = projection.Relationships[relationship];
        GraphRelationshipObservationDocument latest = materialized.LatestObservation;
        IReadOnlyDictionary<string, string> properties = CreateProperties(latest.Properties);
        return new BoundRelationship(
            relationship,
            materialized.FirstDiscoveredAtUtc,
            materialized.LastUpdatedAtUtc,
            properties,
            latest.SourceLabels,
            GraphCypherQueryEngine.WriteProperties(properties));
    }

    private static Dictionary<string, string> CreateProperties(
        IReadOnlyList<GraphPropertyDocument>? properties)
    {
        return properties?.ToDictionary(
            property => property.Name,
            property => property.Value,
            StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private readonly record struct RelationshipBinding(
        GraphEntityKey First,
        GraphEntityKey Second,
        GraphRelationshipKey Relationship);
}
