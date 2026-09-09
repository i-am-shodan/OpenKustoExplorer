namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Describes one persisted value interest.
/// </summary>
public sealed class KustoRecordedInterest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedInterest"/> class.
    /// </summary>
    /// <param name="id">The interest identifier.</param>
    /// <param name="sessionId">The owning session identifier.</param>
    /// <param name="declaredExecutionId">The execution that declared the interest.</param>
    /// <param name="source">How the interest was declared.</param>
    /// <param name="columnName">The predicate or result column name.</param>
    /// <param name="identity">The exact typed identity.</param>
    /// <param name="coordinate">The optional result coordinate for a manual interest.</param>
    /// <param name="literalStart">The optional predicate literal offset.</param>
    /// <param name="literalLength">The optional predicate literal length.</param>
    /// <param name="isSuppressed">Whether this inferred interest is suppressed.</param>
    public KustoRecordedInterest(
        Guid id,
        Guid sessionId,
        Guid declaredExecutionId,
        KustoRecordedInterestSource source,
        string columnName,
        KustoRecordedValueIdentity identity,
        KustoRecordedValueCoordinate? coordinate,
        int? literalStart,
        int? literalLength,
        bool isSuppressed)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(declaredExecutionId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentNullException.ThrowIfNull(identity);
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        Id = id;
        SessionId = sessionId;
        DeclaredExecutionId = declaredExecutionId;
        Source = source;
        ColumnName = columnName.Trim();
        Identity = identity;
        Coordinate = coordinate;
        LiteralStart = literalStart;
        LiteralLength = literalLength;
        IsSuppressed = isSuppressed;
    }

    /// <summary>Gets the interest identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the owning session identifier.</summary>
    public Guid SessionId { get; }

    /// <summary>Gets the execution that declared the interest.</summary>
    public Guid DeclaredExecutionId { get; }

    /// <summary>Gets how the interest was declared.</summary>
    public KustoRecordedInterestSource Source { get; }

    /// <summary>Gets the predicate or result column name.</summary>
    public string ColumnName { get; }

    /// <summary>Gets the exact typed identity.</summary>
    public KustoRecordedValueIdentity Identity { get; }

    /// <summary>Gets the optional result coordinate for a manual interest.</summary>
    public KustoRecordedValueCoordinate? Coordinate { get; }

    /// <summary>Gets the optional predicate literal offset.</summary>
    public int? LiteralStart { get; }

    /// <summary>Gets the optional predicate literal length.</summary>
    public int? LiteralLength { get; }

    /// <summary>Gets a value indicating whether this inferred interest is suppressed.</summary>
    public bool IsSuppressed { get; }
}
