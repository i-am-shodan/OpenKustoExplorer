namespace OpenKustoExplorer.Graph;

/// <summary>
/// Persists a local catalog of named, generation-aware investigation graphs and their complete evidence.
/// </summary>
public interface IGraphStore
{
    /// <summary>
    /// Gets all saved graphs and the sole active graph.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The complete local graph catalog.</returns>
    public Task<GraphCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an empty named graph and activates it atomically.
    /// </summary>
    /// <param name="name">The required analyst-facing graph name.</param>
    /// <param name="description">The optional graph description.</param>
    /// <param name="cancellationToken">A token that cancels and rolls back the operation.</param>
    /// <returns>The new empty active graph state.</returns>
    public Task<GraphStateSummary> CreateGraphAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a graph's analyst-facing name and description.
    /// </summary>
    /// <param name="graphId">The graph to update.</param>
    /// <param name="name">The required analyst-facing graph name.</param>
    /// <param name="description">The optional graph description.</param>
    /// <param name="cancellationToken">A token that cancels and rolls back the operation.</param>
    /// <returns>The updated catalog entry.</returns>
    public Task<GraphCatalogEntry> UpdateGraphAsync(
        Guid graphId,
        string name,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a graph-scoped visible label override for one entity identity.
    /// </summary>
    /// <param name="snapshot">The current graph generation used to pin the mutation.</param>
    /// <param name="entity">The entity to relabel.</param>
    /// <param name="displayLabel">The new analyst-facing label.</param>
    /// <param name="cancellationToken">A token that cancels and rolls back the operation.</param>
    /// <returns>The updated current-generation entity summary.</returns>
    public Task<GraphEntitySummary> SetEntityDisplayLabelAsync(
        GraphSnapshot snapshot,
        GraphEntityKey entity,
        string displayLabel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates a saved graph without changing its contents.
    /// </summary>
    /// <param name="graphId">The graph to activate.</param>
    /// <param name="cancellationToken">A token that cancels and rolls back the operation.</param>
    /// <returns>The activated graph's current state.</returns>
    public Task<GraphStateSummary> ActivateGraphAsync(
        Guid graphId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a saved graph while retaining at least one graph.
    /// </summary>
    /// <param name="graphId">The graph to delete.</param>
    /// <param name="cancellationToken">A token that cancels and rolls back the operation.</param>
    /// <returns>The remaining graph catalog and active selection.</returns>
    public Task<GraphCatalog> DeleteGraphAsync(
        Guid graphId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes every saved graph and replaces the catalog with one empty default graph.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels and rolls back the operation.</param>
    /// <returns>The reset graph catalog and its fresh active selection.</returns>
    public Task<GraphCatalog> DeleteAllGraphsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a count-only summary of the active graph generation.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The active graph state.</returns>
    public Task<GraphStateSummary> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets state for one immutable named graph snapshot.
    /// </summary>
    /// <param name="snapshot">The graph generation to inspect.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The snapshot's graph state.</returns>
    public Task<GraphStateSummary> GetStateAsync(
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest retained properties and cumulative counts for an active-generation entity.
    /// </summary>
    /// <param name="entity">The entity to inspect.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The entity details, or <see langword="null"/> when the entity is not active.</returns>
    public Task<GraphEntityDetails?> GetEntityDetailsAsync(
        GraphEntityKey entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets retained properties and counts for an entity in one current graph snapshot.
    /// </summary>
    /// <param name="snapshot">The graph generation to inspect.</param>
    /// <param name="entity">The entity to inspect.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The entity details, or <see langword="null"/> when the entity is not in the snapshot.</returns>
    public Task<GraphEntityDetails?> GetEntityDetailsAsync(
        GraphSnapshot snapshot,
        GraphEntityKey entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets retained properties and bounded evidence for an active-generation relationship.
    /// </summary>
    /// <param name="relationship">The relationship to inspect.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The relationship details, or <see langword="null"/> when absent.</returns>
    public Task<GraphRelationshipDetails?> GetRelationshipDetailsAsync(
        GraphRelationshipKey relationship,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets retained properties and bounded evidence for a relationship in one graph snapshot.
    /// </summary>
    /// <param name="snapshot">The graph generation to inspect.</param>
    /// <param name="relationship">The relationship to inspect.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The relationship details, or <see langword="null"/> when absent.</returns>
    public Task<GraphRelationshipDetails?> GetRelationshipDetailsAsync(
        GraphSnapshot snapshot,
        GraphRelationshipKey relationship,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets retained investigation timeline points for one named graph, newest first.
    /// </summary>
    /// <param name="graphId">The named graph to inspect.</param>
    /// <param name="maximumPoints">The maximum number of points to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Bounded retained generation starts and ingestion completions.</returns>
    public Task<IReadOnlyList<GraphTimelinePoint>> GetTimelineAsync(
        Guid graphId,
        int maximumPoints,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a bounded historical overview containing graph data known by one retained timeline point.
    /// </summary>
    /// <param name="point">The retained graph timeline point.</param>
    /// <param name="maximumEntityCount">The maximum number of entities to return.</param>
    /// <param name="maximumRelationshipCount">The maximum number of relationships to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The bounded historical graph overview.</returns>
    public Task<GraphViewport> GetTimelineViewportAsync(
        GraphTimelinePoint point,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically adds observations to the active generation or replaces it with a new generation.
    /// </summary>
    /// <param name="source">The validated provenance, evidence, and observation source.</param>
    /// <param name="mode">Whether to add or replace.</param>
    /// <param name="cancellationToken">A token that cancels and rolls back the operation.</param>
    /// <returns>The committed generation and merge counts.</returns>
    public Task<GraphImportResult> ImportAsync(
        IGraphImportSource source,
        GraphImportMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports observations into a graph pinned to its expected current generation.
    /// </summary>
    /// <param name="target">The graph and expected current generation.</param>
    /// <param name="source">The validated provenance, evidence, and observation source.</param>
    /// <param name="mode">Whether to add to or replace the target generation.</param>
    /// <param name="cancellationToken">A token that cancels and rolls back the operation.</param>
    /// <returns>The committed graph generation and merge counts.</returns>
    public Task<GraphImportResult> ImportAsync(
        GraphWriteTarget target,
        IGraphImportSource source,
        GraphImportMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds staged identities that match differently typed nodes in one current graph snapshot.
    /// </summary>
    /// <param name="snapshot">The target graph generation to inspect.</param>
    /// <param name="candidates">The staged identity candidates.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Possible duplicate identity conflicts.</returns>
    public Task<IReadOnlyList<GraphEntityIdentityConflict>> FindIdentityConflictsAsync(
        GraphSnapshot snapshot,
        IEnumerable<GraphEntityIdentityCandidate> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches active-generation entity labels, identifiers, and semantic types without loading evidence.
    /// </summary>
    /// <param name="searchText">The analyst-entered search text.</param>
    /// <param name="maximumResults">The maximum number of summaries to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Matching entity summaries ordered by search relevance.</returns>
    public Task<IReadOnlyList<GraphEntitySummary>> SearchEntitiesAsync(
        string searchText,
        int maximumResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches one current graph snapshot without following later active-graph changes.
    /// </summary>
    /// <param name="snapshot">The graph generation to search.</param>
    /// <param name="searchText">The analyst-entered search text.</param>
    /// <param name="maximumResults">The maximum number of summaries to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Matching entity summaries ordered by search relevance.</returns>
    public Task<IReadOnlyList<GraphEntitySummary>> SearchEntitiesAsync(
        GraphSnapshot snapshot,
        string searchText,
        int maximumResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a bounded active-generation overview or a one-hop viewport around an entity.
    /// </summary>
    /// <param name="center">The entity to center, or <see langword="null"/> to load a bounded overview.</param>
    /// <param name="maximumEntityCount">The maximum number of entities to return.</param>
    /// <param name="maximumRelationshipCount">The maximum number of relationships to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The bounded renderable graph viewport.</returns>
    public Task<GraphViewport> GetViewportAsync(
        GraphEntityKey? center,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a bounded overview or one-hop viewport from one current graph snapshot.
    /// </summary>
    /// <param name="snapshot">The graph generation to project.</param>
    /// <param name="center">The entity to center, or <see langword="null"/> for an overview.</param>
    /// <param name="maximumEntityCount">The maximum number of entities to return.</param>
    /// <param name="maximumRelationshipCount">The maximum number of relationships to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The bounded renderable graph viewport.</returns>
    public Task<GraphViewport> GetViewportAsync(
        GraphSnapshot snapshot,
        GraphEntityKey? center,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a bounded undirected neighborhood around one or more active-generation entities.
    /// </summary>
    /// <param name="centers">The entities whose neighborhoods are merged.</param>
    /// <param name="maximumDepth">The maximum relationship distance from any center.</param>
    /// <param name="maximumEntityCount">The maximum number of entities to return.</param>
    /// <param name="maximumRelationshipCount">The maximum number of relationships to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The bounded renderable graph neighborhood.</returns>
    public Task<GraphViewport> GetNeighborhoodAsync(
        IReadOnlyCollection<GraphEntityKey> centers,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a bounded merged neighborhood from one current graph snapshot.
    /// </summary>
    /// <param name="snapshot">The graph generation to traverse.</param>
    /// <param name="centers">The entities whose neighborhoods are merged.</param>
    /// <param name="maximumDepth">The maximum relationship distance from any center.</param>
    /// <param name="maximumEntityCount">The maximum number of entities to return.</param>
    /// <param name="maximumRelationshipCount">The maximum number of relationships to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The bounded renderable graph neighborhood.</returns>
    public Task<GraphViewport> GetNeighborhoodAsync(
        GraphSnapshot snapshot,
        IReadOnlyCollection<GraphEntityKey> centers,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the bounded union of shortest undirected routes between two entities in one graph snapshot.
    /// </summary>
    /// <param name="snapshot">The graph generation to traverse.</param>
    /// <param name="start">The route start entity.</param>
    /// <param name="destination">The route destination entity.</param>
    /// <param name="maximumEntityCount">The maximum number of route entities to return.</param>
    /// <param name="maximumRelationshipCount">The maximum number of route relationships to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Connectivity and a bounded route viewport.</returns>
    public Task<GraphRouteResult> FindRoutesAsync(
        GraphSnapshot snapshot,
        GraphEntityKey start,
        GraphEntityKey destination,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically activates a new empty graph generation while retaining prior generations for maintenance.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels and rolls back the operation.</param>
    /// <returns>The new empty active graph state.</returns>
    public Task<GraphStateSummary> ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces one pinned graph generation with a new empty generation.
    /// </summary>
    /// <param name="target">The graph and expected current generation.</param>
    /// <param name="cancellationToken">A token that cancels and rolls back the operation.</param>
    /// <returns>The new empty graph state.</returns>
    public Task<GraphStateSummary> ClearAsync(
        GraphWriteTarget target,
        CancellationToken cancellationToken = default);
}
