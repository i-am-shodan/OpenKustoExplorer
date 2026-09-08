namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Summarizes one named recorded query session.
/// </summary>
public sealed class KustoRecordedSessionSummary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedSessionSummary"/> class.
    /// </summary>
    /// <param name="id">The session identifier.</param>
    /// <param name="name">The session name.</param>
    /// <param name="createdAtUtc">The UTC creation time.</param>
    /// <param name="lastUpdatedAtUtc">The UTC last-update time.</param>
    /// <param name="executionCount">The number of recorded executions.</param>
    /// <param name="storedBytes">The logical bytes occupied by the persisted session payload.</param>
    public KustoRecordedSessionSummary(
        Guid id,
        string name,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastUpdatedAtUtc,
        int executionCount,
        long storedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(executionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(storedBytes);
        Id = id;
        Name = name.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        LastUpdatedAtUtc = lastUpdatedAtUtc.ToUniversalTime();
        ExecutionCount = executionCount;
        StoredBytes = storedBytes;
    }

    /// <summary>Gets the session identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the session name.</summary>
    public string Name { get; }

    /// <summary>Gets the UTC creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Gets the UTC last-update time.</summary>
    public DateTimeOffset LastUpdatedAtUtc { get; }

    /// <summary>Gets the number of recorded executions.</summary>
    public int ExecutionCount { get; }

    /// <summary>Gets the logical bytes occupied by the persisted session payload.</summary>
    public long StoredBytes { get; }
}
