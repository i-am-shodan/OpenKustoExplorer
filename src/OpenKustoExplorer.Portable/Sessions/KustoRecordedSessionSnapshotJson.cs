using System.Text.Json;
using System.Text.Json.Serialization;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Sessions;

#pragma warning disable MA0048, SA1402, SA1600, SA1601

namespace OpenKustoExplorer.Portable.Sessions;

internal static class KustoRecordedSessionSnapshotJson
{
    internal const int CurrentVersion = 1;

    internal static KustoRecordedSessionSnapshotDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        KustoRecordedSessionSnapshotDocument? document =
            (KustoRecordedSessionSnapshotDocument?)JsonSerializer.Deserialize(
                json,
                typeof(KustoRecordedSessionSnapshotDocument),
                KustoRecordedSessionSnapshotJsonContext.Default);
        if (document is null || document.Version != CurrentVersion)
        {
            throw new InvalidDataException("The recorded-session snapshot version is unsupported.");
        }

        return document;
    }

    internal static string Serialize(KustoRecordedSessionSnapshotDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(
            document,
            typeof(KustoRecordedSessionSnapshotDocument),
            KustoRecordedSessionSnapshotJsonContext.Default);
    }
}

internal sealed class KustoRecordedSessionSnapshotDocument
{
    public int Version { get; set; } = KustoRecordedSessionSnapshotJson.CurrentVersion;

    public List<KustoRecordedSessionDocument> Sessions { get; set; } = [];
}

internal sealed class KustoRecordedSessionDocument
{
    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<KustoChainEndpointDocument> Endpoints { get; set; } = [];

    public List<KustoRecordedExecutionDocument> Executions { get; set; } = [];

    public Guid Id { get; set; }

    public List<KustoRecordedInterestDocument> Interests { get; set; } = [];

    public DateTimeOffset LastUpdatedAtUtc { get; set; }

    public List<KustoRecordedMarkDocument> Marks { get; set; } = [];

    public string Name { get; set; } = string.Empty;

    public List<KustoRecordingPeriodDocument> Periods { get; set; } = [];
}

internal sealed class KustoRecordingPeriodDocument
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? StoppedAtUtc { get; set; }
}

internal sealed class KustoRecordedExecutionDocument
{
    public string ClusterUri { get; set; } = string.Empty;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string DatabaseName { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public Guid DocumentId { get; set; }

    public string DocumentTitle { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public Guid Id { get; set; }

    public Guid PeriodId { get; set; }

    public string QueryText { get; set; } = string.Empty;

    public KustoRecordedRelationDocument? Relation { get; set; }

    public KustoRecordedResultDocument? Result { get; set; }

    public long Sequence { get; set; }

    public Guid SessionId { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public KustoRecordedExecutionStatus Status { get; set; }
}

internal sealed class KustoRecordedRelationDocument
{
    public List<KustoSourceColumnLineageDocument> Columns { get; set; } = [];

    public bool IsComposable { get; set; }

    public string SourceTableName { get; set; } = string.Empty;
}

internal sealed class KustoSourceColumnLineageDocument
{
    public string ResultColumnName { get; set; } = string.Empty;

    public string SourceColumnName { get; set; } = string.Empty;
}

internal sealed class KustoRecordedResultDocument
{
    public KustoQueryResultCompleteness Completeness { get; set; }

    public long DurationTicks { get; set; }

    public List<KustoRecordedResultTableDocument> Tables { get; set; } = [];
}

internal sealed class KustoRecordedResultTableDocument
{
    public List<KustoRecordedResultColumnDocument> Columns { get; set; } = [];

    public string Name { get; set; } = string.Empty;

    public List<KustoRecordedResultRowDocument> Rows { get; set; } = [];
}

internal sealed class KustoRecordedResultColumnDocument
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;
}

internal sealed class KustoRecordedResultRowDocument
{
    public List<KustoRecordedResultValueDocument> Values { get; set; } = [];
}

internal sealed class KustoRecordedResultValueDocument
{
    public string DisplayText { get; set; } = string.Empty;

    public bool IsNull { get; set; }

    public string? RawJson { get; set; }
}

internal sealed class KustoRecordedInterestDocument
{
    public string CanonicalValue { get; set; } = string.Empty;

    public string ColumnName { get; set; } = string.Empty;

    public KustoRecordedValueCoordinateDocument? Coordinate { get; set; }

    public Guid DeclaredExecutionId { get; set; }

    public Guid Id { get; set; }

    public bool IsNull { get; set; }

    public bool IsSuppressed { get; set; }

    public int? LiteralLength { get; set; }

    public int? LiteralStart { get; set; }

    public Guid? MarkId { get; set; }

    public Guid SessionId { get; set; }

    public KustoRecordedInterestSource Source { get; set; }

    public string TypeName { get; set; } = string.Empty;
}

internal sealed class KustoRecordedMarkDocument
{
    public KustoRecordedValueCoordinateDocument Coordinate { get; set; } = new();

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid Id { get; set; }

    public KustoRecordedMarkKind Kind { get; set; }

    public Guid SessionId { get; set; }
}

internal sealed class KustoChainEndpointDocument
{
    public KustoRecordedValueCoordinateDocument Coordinate { get; set; } = new();

    public KustoChainEndpointRole Role { get; set; }

    public Guid SessionId { get; set; }
}

internal sealed class KustoRecordedValueCoordinateDocument
{
    public int ColumnOrdinal { get; set; }

    public Guid ExecutionId { get; set; }

    public int RowOrdinal { get; set; }

    public int TableOrdinal { get; set; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KustoRecordedSessionSnapshotDocument))]
internal sealed partial class KustoRecordedSessionSnapshotJsonContext : JsonSerializerContext
{
}

#pragma warning restore SA1402, SA1600, SA1601
