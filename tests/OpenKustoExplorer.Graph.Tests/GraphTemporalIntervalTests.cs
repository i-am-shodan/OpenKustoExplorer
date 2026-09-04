namespace OpenKustoExplorer.Graph.Tests;

/// <summary>
/// Verifies graph event-time and discovery-time behavior.
/// </summary>
public sealed class GraphTemporalIntervalTests
{
    /// <summary>
    /// Verifies visibility requires both source validity and discovery knowledge.
    /// </summary>
    [Fact]
    public void VisibilityHonorsEventTimeAndKnownByTime()
    {
        DateTimeOffset validFrom = new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset validTo = validFrom.AddHours(2);
        DateTimeOffset discoveredAt = validFrom.AddMinutes(30);
        GraphTemporalInterval interval = new(discoveredAt, validFrom, validTo);

        Assert.False(interval.IsVisibleAt(validFrom.AddMinutes(10), validFrom.AddMinutes(20)));
        Assert.True(interval.IsVisibleAt(validFrom.AddHours(1), discoveredAt));
        Assert.False(interval.IsVisibleAt(validTo, validTo));
    }

    /// <summary>
    /// Verifies discovery time becomes event time when the source has no temporal property.
    /// </summary>
    [Fact]
    public void DiscoveryTimeIsTheValidTimeFallback()
    {
        DateTimeOffset discoveredAt = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);
        GraphTemporalInterval interval = new(discoveredAt);

        Assert.Equal(discoveredAt, interval.EffectiveValidFromUtc);
        Assert.False(interval.IsVisibleAt(discoveredAt.AddTicks(-1), discoveredAt));
        Assert.True(interval.IsVisibleAt(discoveredAt, discoveredAt));
    }

    /// <summary>
    /// Verifies invalid temporal intervals are rejected rather than silently inverted.
    /// </summary>
    [Fact]
    public void InvalidIntervalsAreRejected()
    {
        DateTimeOffset discoveredAt = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTemporalInterval(
            discoveredAt,
            validFromUtc: discoveredAt,
            validToUtc: discoveredAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTemporalInterval(
            discoveredAt,
            supersededAtUtc: discoveredAt));
    }
}
