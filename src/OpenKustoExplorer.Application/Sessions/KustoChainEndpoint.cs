namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Describes one persisted chain endpoint.
/// </summary>
public sealed class KustoChainEndpoint
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoChainEndpoint"/> class.
    /// </summary>
    /// <param name="sessionId">The owning session identifier.</param>
    /// <param name="role">The endpoint role.</param>
    /// <param name="coordinate">The selected result coordinate.</param>
    public KustoChainEndpoint(
        Guid sessionId,
        KustoChainEndpointRole role,
        KustoRecordedValueCoordinate coordinate)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(coordinate);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        SessionId = sessionId;
        Role = role;
        Coordinate = coordinate;
    }

    /// <summary>Gets the owning session identifier.</summary>
    public Guid SessionId { get; }

    /// <summary>Gets the endpoint role.</summary>
    public KustoChainEndpointRole Role { get; }

    /// <summary>Gets the selected coordinate.</summary>
    public KustoRecordedValueCoordinate Coordinate { get; }
}
