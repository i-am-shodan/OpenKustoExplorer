namespace OpenKustoExplorer.Graph.Tests;

/// <summary>
/// Verifies directed and parallel graph relationship identity behavior.
/// </summary>
public sealed class GraphRelationshipKeyTests
{
    /// <summary>
    /// Verifies direction is part of a relationship identity.
    /// </summary>
    [Fact]
    public void ReversedRelationshipIsDistinct()
    {
        GraphEntityKey user = new(GraphEntityKind.User, "User", "alice");
        GraphEntityKey host = new(GraphEntityKind.Host, "Host", "server-1");
        GraphRelationshipKey outbound = new(user, host, "AuthenticatedTo");
        GraphRelationshipKey inbound = new(host, user, "AuthenticatedTo");

        Assert.NotEqual(outbound, inbound);
    }

    /// <summary>
    /// Verifies an explicit discriminator preserves independent parallel relationships.
    /// </summary>
    [Fact]
    public void DiscriminatorPreservesParallelRelationships()
    {
        GraphEntityKey source = new(GraphEntityKind.IpAddress, "IPAddress", "192.0.2.1");
        GraphEntityKey target = new(GraphEntityKind.Host, "Host", "server-1");
        GraphRelationshipKey first = new(source, target, "ConnectedTo", "event-1");
        GraphRelationshipKey second = new(source, target, "ConnectedTo", "event-2");

        Assert.NotEqual(first, second);
        Assert.EndsWith("#event-1", first.ToString(), StringComparison.Ordinal);
    }
}
