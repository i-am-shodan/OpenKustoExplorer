namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Contains the selected weighted sequence of inferred pivots between two recorded values.
/// </summary>
public sealed class KustoQueryChain
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoQueryChain"/> class.
    /// </summary>
    /// <param name="sessionId">The owning session identifier.</param>
    /// <param name="start">The selected start coordinate.</param>
    /// <param name="end">The selected end coordinate.</param>
    /// <param name="pivots">The selected pivot evidence in traversal order.</param>
    /// <param name="totalCost">The total evidence cost.</param>
    public KustoQueryChain(
        Guid sessionId,
        KustoRecordedValueCoordinate start,
        KustoRecordedValueCoordinate end,
        IEnumerable<KustoPivotEvidence> pivots,
        int totalCost)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        ArgumentNullException.ThrowIfNull(pivots);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCost);
        SessionId = sessionId;
        Start = start;
        End = end;
        Pivots = Array.AsReadOnly(pivots.ToArray());
        TotalCost = totalCost;
    }

    /// <summary>Gets the owning session identifier.</summary>
    public Guid SessionId { get; }

    /// <summary>Gets the selected start coordinate.</summary>
    public KustoRecordedValueCoordinate Start { get; }

    /// <summary>Gets the selected end coordinate.</summary>
    public KustoRecordedValueCoordinate End { get; }

    /// <summary>Gets selected pivot evidence in traversal order.</summary>
    public IReadOnlyList<KustoPivotEvidence> Pivots { get; }

    /// <summary>Gets the total evidence cost.</summary>
    public int TotalCost { get; }
}
