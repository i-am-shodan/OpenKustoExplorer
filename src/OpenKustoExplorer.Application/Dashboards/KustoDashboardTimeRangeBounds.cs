namespace OpenKustoExplorer.Application.Dashboards;

/// <summary>
/// Contains resolved UTC start and end values for one dashboard refresh.
/// </summary>
public sealed class KustoDashboardTimeRangeBounds
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardTimeRangeBounds"/> class.
    /// </summary>
    /// <param name="startUtc">The inclusive UTC start.</param>
    /// <param name="endUtc">The inclusive UTC end.</param>
    public KustoDashboardTimeRangeBounds(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (startUtc >= endUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(startUtc), "The start time must precede the end time.");
        }

        StartUtc = startUtc.ToUniversalTime();
        EndUtc = endUtc.ToUniversalTime();
    }

    /// <summary>
    /// Gets the inclusive UTC start.
    /// </summary>
    public DateTimeOffset StartUtc { get; }

    /// <summary>
    /// Gets the inclusive UTC end.
    /// </summary>
    public DateTimeOffset EndUtc { get; }
}
