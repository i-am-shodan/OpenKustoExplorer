using OpenKustoExplorer.Application.Dashboards;

namespace OpenKustoExplorer.Application.Tests.Dashboards;

/// <summary>
/// Verifies dashboard time-range validation and UTC resolution.
/// </summary>
public sealed class KustoDashboardTimeRangeTests
{
    /// <summary>
    /// Verifies a relative range resolves against the supplied refresh time.
    /// </summary>
    [Fact]
    public void RelativeRangeResolvesAgainstSharedRefreshTime()
    {
        DateTimeOffset utcNow = new(2026, 9, 16, 12, 30, 0, TimeSpan.Zero);

        KustoDashboardTimeRangeBounds bounds = KustoDashboardTimeRange
            .CreateRelative(TimeSpan.FromHours(6))
            .Resolve(utcNow);

        Assert.Equal(utcNow.AddHours(-6), bounds.StartUtc);
        Assert.Equal(utcNow, bounds.EndUtc);
    }

    /// <summary>
    /// Verifies absolute values are normalized to UTC and do not slide.
    /// </summary>
    [Fact]
    public void AbsoluteRangeNormalizesAndRetainsBounds()
    {
        DateTimeOffset start = new(2026, 9, 15, 8, 0, 0, TimeSpan.FromHours(-4));
        DateTimeOffset end = start.AddHours(2);
        KustoDashboardTimeRange range = KustoDashboardTimeRange.CreateAbsolute(start, end);

        KustoDashboardTimeRangeBounds bounds = range.Resolve(DateTimeOffset.UtcNow);

        Assert.Equal(start.ToUniversalTime(), bounds.StartUtc);
        Assert.Equal(end.ToUniversalTime(), bounds.EndUtc);
    }

    /// <summary>
    /// Verifies dashboards without an explicit definition use Last 24 hours.
    /// </summary>
    [Fact]
    public void DashboardDefaultsToLast24Hours()
    {
        KustoDashboard dashboard = new(Guid.NewGuid(), "Operations", "#FFFFFF", []);

        Assert.Equal(KustoDashboardTimeRangeKind.Relative, dashboard.TimeRange.Kind);
        Assert.Equal(TimeSpan.FromHours(24), dashboard.TimeRange.RelativeDuration);
    }

    /// <summary>
    /// Verifies non-positive relative ranges are rejected.
    /// </summary>
    [Fact]
    public void RelativeRangeRejectsNonPositiveDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => KustoDashboardTimeRange.CreateRelative(TimeSpan.Zero));
    }

    /// <summary>
    /// Verifies fixed ranges require ordered endpoints.
    /// </summary>
    [Fact]
    public void AbsoluteRangeRejectsUnorderedEndpoints()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => KustoDashboardTimeRange.CreateAbsolute(timestamp, timestamp));
    }

    /// <summary>
    /// Verifies custom ranges reject a local time skipped by daylight saving time.
    /// </summary>
    [Fact]
    public void CustomRangeRejectsInvalidLocalTime()
    {
        TimeZoneInfo timeZone = CreateDaylightSavingTimeZone();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => KustoDashboardTimeRange.CreateAbsoluteFromLocal(
                new DateTime(2026, 3, 8, 2, 30, 0, DateTimeKind.Unspecified),
                new DateTime(2026, 3, 8, 4, 0, 0, DateTimeKind.Unspecified),
                timeZone));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies custom ranges reject a local time repeated by daylight saving time.
    /// </summary>
    [Fact]
    public void CustomRangeRejectsAmbiguousLocalTime()
    {
        TimeZoneInfo timeZone = CreateDaylightSavingTimeZone();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => KustoDashboardTimeRange.CreateAbsoluteFromLocal(
                new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified),
                new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Unspecified),
                timeZone));

        Assert.Contains("ambiguous", exception.Message, StringComparison.Ordinal);
    }

    private static TimeZoneInfo CreateDaylightSavingTimeZone()
    {
        TimeZoneInfo.TransitionTime daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified),
            3,
            2,
            DayOfWeek.Sunday);
        TimeZoneInfo.TransitionTime daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified),
            11,
            1,
            DayOfWeek.Sunday);
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone(
            "OpenKustoExplorer.Tests.DashboardTimeRange",
            TimeSpan.Zero,
            "Dashboard test time",
            "Dashboard test standard time",
            "Dashboard test daylight time",
            [rule]);
    }
}
