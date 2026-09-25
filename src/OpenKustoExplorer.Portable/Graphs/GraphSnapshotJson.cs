using System.Text.Json;
using System.Text.Json.Serialization;
using OpenKustoExplorer.Graph;

#pragma warning disable MA0048, SA1402, SA1600, SA1601

namespace OpenKustoExplorer.Portable.Graphs;

internal static class GraphSnapshotJson
{
    internal const int CurrentVersion = 1;

    internal static GraphSnapshotDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        GraphSnapshotDocument? document = (GraphSnapshotDocument?)JsonSerializer.Deserialize(
            json,
            typeof(GraphSnapshotDocument),
            GraphSnapshotJsonContext.Default);
        if (document is null || document.Version != CurrentVersion)
        {
            throw new InvalidDataException("The graph snapshot version is unsupported.");
        }

        return document;
    }

    internal static string Serialize(GraphSnapshotDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(
            document,
            typeof(GraphSnapshotDocument),
            GraphSnapshotJsonContext.Default);
    }
}

internal sealed class GraphSnapshotDocument
{
    public Guid ActiveGraphId { get; set; }

    public List<GraphDocument> Graphs { get; set; } = [];

    public int Version { get; set; } = GraphSnapshotJson.CurrentVersion;
}

internal sealed class GraphDocument
{
    public Guid ActiveGenerationId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string Description { get; set; } = string.Empty;

    public List<GraphGenerationDocument> Generations { get; set; } = [];

    public Guid Id { get; set; }

    public List<GraphLabelOverrideDocument> LabelOverrides { get; set; } = [];

    public DateTimeOffset LastActivatedAtUtc { get; set; }

    public DateTimeOffset LastUpdatedAtUtc { get; set; }

    public string Name { get; set; } = string.Empty;
}

internal sealed class GraphGenerationDocument
{
    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid Id { get; set; }

    public List<GraphIngestionDocument> Ingestions { get; set; } = [];

    public DateTimeOffset LastUpdatedAtUtc { get; set; }
}

internal sealed class GraphIngestionDocument
{
    public string ClusterUri { get; set; } = string.Empty;

    public DateTimeOffset CompletedAtUtc { get; set; }

    public string DatabaseName { get; set; } = string.Empty;

    public List<GraphEntityObservationDocument> EntityObservations { get; set; } = [];

    public List<GraphEvidenceDocument> Evidence { get; set; } = [];

    public Guid Id { get; set; }

    public string QueryText { get; set; } = string.Empty;

    public List<GraphRelationshipObservationDocument> RelationshipObservations { get; set; } = [];

    public Guid? SourceId { get; set; }

    public GraphIngestionSourceKind SourceKind { get; set; }

    public string SourceName { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }
}

internal sealed class GraphEvidenceDocument
{
    public string ContentHash { get; set; } = string.Empty;

    public string OccurrenceId { get; set; } = string.Empty;

    public string RowJson { get; set; } = string.Empty;

    public int RowOrdinal { get; set; }

    public string SchemaJson { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;
}

internal sealed class GraphEntityObservationDocument
{
    public GraphEntityKeyDocument Entity { get; set; } = new();

    public List<string> EvidenceIds { get; set; } = [];

    public Guid Id { get; set; }

    public string DisplayLabel { get; set; } = string.Empty;

    public List<GraphPropertyDocument> Properties { get; set; } = [];

    public List<string> SourceLabels { get; set; } = [];

    public GraphTemporalIntervalDocument TemporalInterval { get; set; } = new();
}

internal sealed class GraphRelationshipObservationDocument
{
    public List<string> EvidenceIds { get; set; } = [];

    public Guid Id { get; set; }

    public List<GraphPropertyDocument> Properties { get; set; } = [];

    public GraphRelationshipKeyDocument Relationship { get; set; } = new();

    public List<string> SourceLabels { get; set; } = [];

    public GraphTemporalIntervalDocument TemporalInterval { get; set; } = new();
}

internal sealed class GraphEntityKeyDocument
{
    public string CanonicalId { get; set; } = string.Empty;

    public GraphEntityKind Kind { get; set; }

    public string SourceNamespace { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;
}

internal sealed class GraphRelationshipKeyDocument
{
    public string Discriminator { get; set; } = string.Empty;

    public GraphEntityKeyDocument Source { get; set; } = new();

    public GraphEntityKeyDocument Target { get; set; } = new();

    public string TypeName { get; set; } = string.Empty;
}

internal sealed class GraphTemporalIntervalDocument
{
    public DateTimeOffset DiscoveredAtUtc { get; set; }

    public DateTimeOffset? SupersededAtUtc { get; set; }

    public DateTimeOffset? ValidFromUtc { get; set; }

    public DateTimeOffset? ValidToUtc { get; set; }
}

internal sealed class GraphPropertyDocument
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

internal sealed class GraphLabelOverrideDocument
{
    public string DisplayLabel { get; set; } = string.Empty;

    public GraphEntityKeyDocument Entity { get; set; } = new();
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GraphSnapshotDocument))]
internal sealed partial class GraphSnapshotJsonContext : JsonSerializerContext
{
}

#pragma warning restore SA1402, SA1600, SA1601
