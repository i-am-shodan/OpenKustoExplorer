namespace OpenKustoExplorer.Application.Dashboards;

/// <summary>
/// Describes one validated relative or absolute dashboard time range.
/// </summary>
public sealed class KustoDashboardTimeRange
{
    private KustoDashboardTimeRange(
        KustoDashboardTimeRangeKind kind,
        TimeSpan? relativeDuration,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtc)
    {
        Kind = kind;
        RelativeDuration = relativeDuration;
        StartUtc = startUtc;
        EndUtc = endUtc;
    }

    /// <summary>
    /// Gets the default sliding dashboard range.
    /// </summary>
    public static KustoDashboardTimeRange Last24Hours { get; } = CreateRelative(TimeSpan.FromHours(24));

    /// <summary>
    /// Gets the range kind.
    /// </summary>
    public KustoDashboardTimeRangeKind Kind { get; }

    /// <summary>
    /// Gets the sliding duration when <see cref="Kind"/> is <see cref="KustoDashboardTimeRangeKind.Relative"/>.
    /// </summary>
    public TimeSpan? RelativeDuration { get; }

    /// <summary>
    /// Gets the fixed UTC start when <see cref="Kind"/> is <see cref="KustoDashboardTimeRangeKind.Absolute"/>.
    /// </summary>
    public DateTimeOffset? StartUtc { get; }

    /// <summary>
    /// Gets the fixed UTC end when <see cref="Kind"/> is <see cref="KustoDashboardTimeRangeKind.Absolute"/>.
    /// </summary>
    public DateTimeOffset? EndUtc { get; }

    /// <summary>
    /// Creates a sliding time range.
    /// </summary>
    /// <param name="duration">The positive duration ending at refresh time.</param>
    /// <returns>The validated range.</returns>
    public static KustoDashboardTimeRange CreateRelative(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        return new KustoDashboardTimeRange(
            KustoDashboardTimeRangeKind.Relative,
            duration,
            null,
            null);
    }

    /// <summary>
    /// Creates a fixed range from UTC-normalized endpoints.
    /// </summary>
    /// <param name="startUtc">The inclusive start.</param>
    /// <param name="endUtc">The inclusive end.</param>
    /// <returns>The validated range.</returns>
    public static KustoDashboardTimeRange CreateAbsolute(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        KustoDashboardTimeRangeBounds bounds = new(startUtc, endUtc);
        return new KustoDashboardTimeRange(
            KustoDashboardTimeRangeKind.Absolute,
            null,
            bounds.StartUtc,
            bounds.EndUtc);
    }

    /// <summary>
    /// Creates a fixed range from local wall-clock values.
    /// </summary>
    /// <param name="startLocal">The local start without an embedded offset.</param>
    /// <param name="endLocal">The local end without an embedded offset.</param>
    /// <param name="timeZone">The time zone used to resolve both values.</param>
    /// <returns>The validated UTC range.</returns>
    public static KustoDashboardTimeRange CreateAbsoluteFromLocal(
        DateTime startLocal,
        DateTime endLocal,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        DateTime normalizedStart = DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified);
        DateTime normalizedEnd = DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified);
        ValidateLocalTime(normalizedStart, timeZone, nameof(startLocal));
        ValidateLocalTime(normalizedEnd, timeZone, nameof(endLocal));
        return CreateAbsolute(
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(normalizedStart, timeZone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(normalizedEnd, timeZone)));
    }

    /// <summary>
    /// Resolves this definition for one dashboard refresh.
    /// </summary>
    /// <param name="utcNow">The shared refresh time.</param>
    /// <returns>The fixed UTC bounds for the refresh.</returns>
    public KustoDashboardTimeRangeBounds Resolve(DateTimeOffset utcNow)
    {
        DateTimeOffset normalizedNow = utcNow.ToUniversalTime();
        return Kind switch
        {
            KustoDashboardTimeRangeKind.Relative => new KustoDashboardTimeRangeBounds(
                normalizedNow.Subtract(RelativeDuration!.Value),
                normalizedNow),
            KustoDashboardTimeRangeKind.Absolute => new KustoDashboardTimeRangeBounds(
                StartUtc!.Value,
                EndUtc!.Value),
            _ => throw new InvalidOperationException($"Unsupported dashboard time range kind {Kind}."),
        };
    }

    private static void ValidateLocalTime(DateTime value, TimeZoneInfo timeZone, string parameterName)
    {
        if (timeZone.IsInvalidTime(value))
        {
            throw new ArgumentException("The local time does not exist in the selected time zone.", parameterName);
        }

        if (timeZone.IsAmbiguousTime(value))
        {
            throw new ArgumentException("The local time is ambiguous in the selected time zone.", parameterName);
        }
    }
}
