namespace OpenKustoExplorer.Graph;

/// <summary>
/// Summarizes one searchable entity identity without loading its observations or evidence payloads.
/// </summary>
public sealed class GraphEntitySummary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphEntitySummary"/> class.
    /// </summary>
    /// <param name="entity">The entity identity.</param>
    /// <param name="displayLabel">The latest display label.</param>
    /// <param name="firstDiscoveredAtUtc">When the entity first entered this generation.</param>
    /// <param name="lastUpdatedAtUtc">When the entity was most recently observed.</param>
    /// <param name="degree">The number of incoming and outgoing relationship identities.</param>
    public GraphEntitySummary(
        GraphEntityKey entity,
        string displayLabel,
        DateTimeOffset firstDiscoveredAtUtc,
        DateTimeOffset lastUpdatedAtUtc,
        long degree)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLabel);
        ArgumentOutOfRangeException.ThrowIfNegative(degree);
        DateTimeOffset normalizedFirstDiscovery = firstDiscoveredAtUtc.ToUniversalTime();
        DateTimeOffset normalizedLastUpdate = lastUpdatedAtUtc.ToUniversalTime();

        if (normalizedLastUpdate < normalizedFirstDiscovery)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastUpdatedAtUtc),
                "Entity update time cannot precede first discovery.");
        }

        Entity = entity;
        DisplayLabel = displayLabel.Trim();
        FirstDiscoveredAtUtc = normalizedFirstDiscovery;
        LastUpdatedAtUtc = normalizedLastUpdate;
        Degree = degree;
    }

    /// <summary>
    /// Gets the entity identity.
    /// </summary>
    public GraphEntityKey Entity { get; }

    /// <summary>
    /// Gets the latest display label.
    /// </summary>
    public string DisplayLabel { get; }

    /// <summary>
    /// Gets when the entity first entered this generation.
    /// </summary>
    public DateTimeOffset FirstDiscoveredAtUtc { get; }

    /// <summary>
    /// Gets when the entity was most recently observed.
    /// </summary>
    public DateTimeOffset LastUpdatedAtUtc { get; }

    /// <summary>
    /// Gets the number of incoming and outgoing relationship identities.
    /// </summary>
    public long Degree { get; }
}
