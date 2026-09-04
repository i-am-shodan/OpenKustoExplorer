namespace OpenKustoExplorer.Graph.Tests;

/// <summary>
/// Verifies graph observations preserve immutable source and evidence snapshots.
/// </summary>
public sealed class GraphObservationTests
{
    /// <summary>
    /// Verifies caller-owned collections cannot rewrite an entity observation after construction.
    /// </summary>
    [Fact]
    public void EntityObservationDefensivelyCopiesCollections()
    {
        List<string> labels = ["AZUser", "AZUser"];
        Dictionary<string, string> properties = new(StringComparer.Ordinal)
        {
            ["displayName"] = "Alice",
        };
        List<string> evidenceIds = ["row-1", "row-1"];
        GraphEntityObservation observation = new(
            Guid.NewGuid(),
            new GraphEntityKey(GraphEntityKind.User, "User", "alice@example.com"),
            "Alice",
            labels,
            properties,
            new GraphTemporalInterval(DateTimeOffset.UtcNow),
            evidenceIds);

        labels.Add("Changed");
        properties["displayName"] = "Changed";
        evidenceIds.Add("row-2");

        Assert.Equal(["AZUser"], observation.SourceLabels);
        Assert.Equal("Alice", observation.Properties["displayName"]);
        Assert.Equal(["row-1"], observation.EvidenceIds);
    }

    /// <summary>
    /// Verifies relationship observations preserve both endpoint identity and evidence.
    /// </summary>
    [Fact]
    public void RelationshipObservationRetainsEvidenceLink()
    {
        GraphEntityKey source = new(GraphEntityKind.User, "User", "alice");
        GraphEntityKey target = new(GraphEntityKind.Database, "Database", "security");
        GraphRelationshipKey relationship = new(source, target, "CanRead");
        GraphRelationshipObservation observation = new(
            Guid.NewGuid(),
            relationship,
            ["AZCanRead"],
            new Dictionary<string, string>(StringComparer.Ordinal),
            new GraphTemporalInterval(DateTimeOffset.UtcNow),
            ["row-42"]);

        Assert.Equal(relationship, observation.Relationship);
        Assert.Equal("row-42", Assert.Single(observation.EvidenceIds));
    }
}
