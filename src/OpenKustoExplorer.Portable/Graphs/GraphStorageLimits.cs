namespace OpenKustoExplorer.Portable.Graphs;

/// <summary>
/// Defines graph persistence and interactive projection limits shared by all hosts.
/// </summary>
public static class GraphStorageLimits
{
    /// <summary>Gets the maximum entities retained in one active graph generation.</summary>
    public const int MaximumEntityCount = 25_000;

    /// <summary>Gets the maximum relationships retained in one active graph generation.</summary>
    public const int MaximumRelationshipCount = 100_000;

    /// <summary>Gets the maximum entities returned by an interactive viewport.</summary>
    public const int MaximumViewportEntityCount = 500;

    /// <summary>Gets the maximum relationships returned by an interactive viewport.</summary>
    public const int MaximumViewportRelationshipCount = 2_000;

    /// <summary>Gets the maximum entity search results.</summary>
    public const int MaximumSearchResults = 500;

    /// <summary>Gets the maximum graph-neighborhood traversal depth.</summary>
    public const int MaximumNeighborhoodDepth = 10;
}
