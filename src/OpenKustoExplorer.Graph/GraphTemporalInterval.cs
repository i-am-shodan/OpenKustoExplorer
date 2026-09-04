namespace OpenKustoExplorer.Graph;

/// <summary>
/// Describes when an observation was valid in its source and when the investigation knew about it.
/// </summary>
public sealed class GraphTemporalInterval
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphTemporalInterval"/> class.
    /// </summary>
    /// <param name="discoveredAtUtc">When the observation entered the local investigation graph.</param>
    /// <param name="validFromUtc">When the observation became valid in its source, if known.</param>
    /// <param name="validToUtc">When the observation stopped being valid in its source, if known.</param>
    /// <param name="supersededAtUtc">When a later discovery superseded this observation, if applicable.</param>
    public GraphTemporalInterval(
        DateTimeOffset discoveredAtUtc,
        DateTimeOffset? validFromUtc = null,
        DateTimeOffset? validToUtc = null,
        DateTimeOffset? supersededAtUtc = null)
    {
        DateTimeOffset normalizedDiscovery = discoveredAtUtc.ToUniversalTime();
        DateTimeOffset? normalizedValidFrom = validFromUtc?.ToUniversalTime();
        DateTimeOffset? normalizedValidTo = validToUtc?.ToUniversalTime();
        DateTimeOffset? normalizedSuperseded = supersededAtUtc?.ToUniversalTime();
        DateTimeOffset effectiveValidFrom = normalizedValidFrom ?? normalizedDiscovery;

        if (normalizedValidTo <= effectiveValidFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(validToUtc), "Valid-to time must follow valid-from time.");
        }

        if (normalizedSuperseded <= normalizedDiscovery)
        {
            throw new ArgumentOutOfRangeException(
                nameof(supersededAtUtc),
                "Superseded time must follow discovery time.");
        }

        DiscoveredAtUtc = normalizedDiscovery;
        ValidFromUtc = normalizedValidFrom;
        ValidToUtc = normalizedValidTo;
        SupersededAtUtc = normalizedSuperseded;
    }

    /// <summary>
    /// Gets when the observation entered the local investigation graph.
    /// </summary>
    public DateTimeOffset DiscoveredAtUtc { get; }

    /// <summary>
    /// Gets when the observation became valid in its source, or <see langword="null"/> when source time is unknown.
    /// </summary>
    public DateTimeOffset? ValidFromUtc { get; }

    /// <summary>
    /// Gets when the observation stopped being valid in its source, or <see langword="null"/> while open-ended.
    /// </summary>
    public DateTimeOffset? ValidToUtc { get; }

    /// <summary>
    /// Gets when a later discovery superseded this observation, or <see langword="null"/> while current.
    /// </summary>
    public DateTimeOffset? SupersededAtUtc { get; }

    /// <summary>
    /// Gets source-valid time with discovery time as the documented fallback.
    /// </summary>
    public DateTimeOffset EffectiveValidFromUtc => ValidFromUtc ?? DiscoveredAtUtc;

    /// <summary>
    /// Determines whether this observation was valid at an event time and known by a discovery time.
    /// </summary>
    /// <param name="eventTimeUtc">The source event time to inspect.</param>
    /// <param name="knownByUtc">The latest discovery time the analyst may use.</param>
    /// <returns><see langword="true"/> when both bitemporal dimensions include the requested point.</returns>
    public bool IsVisibleAt(DateTimeOffset eventTimeUtc, DateTimeOffset knownByUtc)
    {
        DateTimeOffset normalizedEventTime = eventTimeUtc.ToUniversalTime();
        DateTimeOffset normalizedKnownBy = knownByUtc.ToUniversalTime();
        bool isValid = EffectiveValidFromUtc <= normalizedEventTime
            && (ValidToUtc is null || normalizedEventTime < ValidToUtc);
        bool wasKnown = DiscoveredAtUtc <= normalizedKnownBy
            && (SupersededAtUtc is null || normalizedKnownBy < SupersededAtUtc);
        return isValid && wasKnown;
    }
}
