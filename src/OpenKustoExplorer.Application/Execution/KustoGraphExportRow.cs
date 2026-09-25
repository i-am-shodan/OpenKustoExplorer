using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Holds one normalized node or edge row before durable graph import.
/// </summary>
public sealed class KustoGraphExportRow
{
    private KustoGraphExportRow(
        KustoGraphExportTableKind kind,
        int rowOrdinal,
        string rowJson,
        string contentHash)
    {
        Kind = kind;
        RowOrdinal = rowOrdinal;
        RowJson = rowJson;
        ContentHash = contentHash;
    }

    /// <summary>Gets the canonical entity identifier for a node row.</summary>
    public string CanonicalId { get; private init; } = string.Empty;

    /// <summary>Gets the deterministic row content hash.</summary>
    public string ContentHash { get; }

    /// <summary>Gets the relationship discriminator for an edge row.</summary>
    public string Discriminator { get; private init; } = string.Empty;

    /// <summary>Gets the preferred entity display label for a node row.</summary>
    public string DisplayLabel { get; private init; } = string.Empty;

    /// <summary>Gets the inferred entity kind for a node row.</summary>
    public GraphEntityKind EntityKind { get; private init; }

    /// <summary>Gets whether the row contains a node or edge.</summary>
    public KustoGraphExportTableKind Kind { get; }

    /// <summary>Gets the generated node hash for a node row.</summary>
    public string NodeHash { get; private init; } = string.Empty;

    /// <summary>Gets the relationship type for an edge row.</summary>
    public string RelationshipType { get; private init; } = string.Empty;

    /// <summary>Gets the canonical raw row JSON.</summary>
    public string RowJson { get; }

    /// <summary>Gets the zero-based source row position.</summary>
    public int RowOrdinal { get; }

    /// <summary>Gets the original source entity type label for a node row.</summary>
    public string SourceTypeName { get; private init; } = string.Empty;

    /// <summary>Gets the generated source node hash for an edge row.</summary>
    public string SourceHash { get; private init; } = string.Empty;

    /// <summary>Gets the generated target node hash for an edge row.</summary>
    public string TargetHash { get; private init; } = string.Empty;

    /// <summary>Gets the normalized entity type name for a node row.</summary>
    public string TypeName { get; private init; } = string.Empty;

    /// <summary>
    /// Creates a normalized edge row.
    /// </summary>
    /// <param name="rowOrdinal">The zero-based source row position.</param>
    /// <param name="rowJson">The canonical raw row JSON.</param>
    /// <param name="contentHash">The deterministic row content hash.</param>
    /// <param name="sourceHash">The generated source node hash.</param>
    /// <param name="targetHash">The generated target node hash.</param>
    /// <param name="relationshipType">The normalized relationship type.</param>
    /// <param name="discriminator">The normalized relationship discriminator.</param>
    /// <returns>The normalized row.</returns>
    internal static KustoGraphExportRow CreateEdge(
        int rowOrdinal,
        string rowJson,
        string contentHash,
        string sourceHash,
        string targetHash,
        string relationshipType,
        string discriminator)
    {
        return new KustoGraphExportRow(
            KustoGraphExportTableKind.Edges,
            rowOrdinal,
            rowJson,
            contentHash)
        {
            Discriminator = discriminator,
            RelationshipType = relationshipType,
            SourceHash = sourceHash,
            TargetHash = targetHash,
        };
    }

    /// <summary>
    /// Creates a normalized node row.
    /// </summary>
    /// <param name="rowOrdinal">The zero-based source row position.</param>
    /// <param name="rowJson">The canonical raw row JSON.</param>
    /// <param name="contentHash">The deterministic row content hash.</param>
    /// <param name="nodeHash">The generated node hash.</param>
    /// <param name="entityKind">The inferred entity kind.</param>
    /// <param name="typeName">The normalized entity type name.</param>
    /// <param name="canonicalId">The canonical entity identifier.</param>
    /// <param name="displayLabel">The preferred entity display label.</param>
    /// <returns>The normalized row.</returns>
    internal static KustoGraphExportRow CreateNode(
        int rowOrdinal,
        string rowJson,
        string contentHash,
        string nodeHash,
        GraphEntityKind entityKind,
        string typeName,
        string canonicalId,
        string displayLabel)
    {
        return new KustoGraphExportRow(
            KustoGraphExportTableKind.Nodes,
            rowOrdinal,
            rowJson,
            contentHash)
        {
            CanonicalId = canonicalId,
            DisplayLabel = displayLabel,
            EntityKind = entityKind,
            NodeHash = nodeHash,
            SourceTypeName = typeName,
            TypeName = typeName,
        };
    }
}
