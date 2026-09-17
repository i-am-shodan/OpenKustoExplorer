namespace OpenKustoExplorer.Graph;

/// <summary>
/// Identifies one retained point in a named graph's investigation history.
/// </summary>
public sealed class GraphTimelinePoint
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphTimelinePoint"/> class.
    /// </summary>
    /// <param name="snapshot">The retained graph generation.</param>
    /// <param name="timestampUtc">The latest discovery time visible at this point.</param>
    /// <param name="ingestionId">The ingestion that produced this point, or <see langword="null"/> for an empty generation.</param>
    /// <param name="sourceKind">The originating workflow, or <see langword="null"/> for an empty generation.</param>
    /// <param name="sourceName">The analyst-facing source or generation label.</param>
    public GraphTimelinePoint(
        GraphSnapshot snapshot,
        DateTimeOffset timestampUtc,
        Guid? ingestionId,
        GraphIngestionSourceKind? sourceKind,
        string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        if (ingestionId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(ingestionId));
        }

        if (ingestionId.HasValue != sourceKind.HasValue)
        {
            string parameterName = ingestionId.HasValue ? nameof(sourceKind) : nameof(ingestionId);
            throw new ArgumentException(
                "Timeline ingestion identity and source kind must either both be set or both be absent.",
                parameterName);
        }

        if (sourceKind is not null && !Enum.IsDefined(sourceKind.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }

        Snapshot = snapshot;
        TimestampUtc = timestampUtc.ToUniversalTime();
        IngestionId = ingestionId;
        SourceKind = sourceKind;
        SourceName = sourceName.Trim();
    }

    /// <summary>
    /// Gets the ingestion that produced this point, or <see langword="null"/> for an empty generation.
    /// </summary>
    public Guid? IngestionId { get; }

    /// <summary>
    /// Gets a value indicating whether this point represents an empty generation start.
    /// </summary>
    public bool IsGenerationStart => IngestionId is null;

    /// <summary>
    /// Gets the retained graph generation.
    /// </summary>
    public GraphSnapshot Snapshot { get; }

    /// <summary>
    /// Gets the originating workflow, or <see langword="null"/> for an empty generation.
    /// </summary>
    public GraphIngestionSourceKind? SourceKind { get; }

    /// <summary>
    /// Gets the analyst-facing source or generation label.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Gets the latest discovery time visible at this point.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; }
}
