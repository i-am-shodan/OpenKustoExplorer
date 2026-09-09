namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Describes one persisted user mark.
/// </summary>
public sealed class KustoRecordedMark
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedMark"/> class.
    /// </summary>
    /// <param name="id">The mark identifier.</param>
    /// <param name="sessionId">The owning session identifier.</param>
    /// <param name="kind">The mark scope.</param>
    /// <param name="coordinate">The marked result coordinate.</param>
    /// <param name="createdAtUtc">The UTC creation time.</param>
    public KustoRecordedMark(
        Guid id,
        Guid sessionId,
        KustoRecordedMarkKind kind,
        KustoRecordedValueCoordinate coordinate,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(coordinate);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Id = id;
        SessionId = sessionId;
        Kind = kind;
        Coordinate = coordinate;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    /// <summary>Gets the mark identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the owning session identifier.</summary>
    public Guid SessionId { get; }

    /// <summary>Gets the mark scope.</summary>
    public KustoRecordedMarkKind Kind { get; }

    /// <summary>Gets the marked coordinate.</summary>
    public KustoRecordedValueCoordinate Coordinate { get; }

    /// <summary>Gets the UTC creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; }
}
