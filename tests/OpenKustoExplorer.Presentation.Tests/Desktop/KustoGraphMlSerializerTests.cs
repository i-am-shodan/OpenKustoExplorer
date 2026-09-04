using System.Text;
using System.Xml.Linq;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Desktop.Graphs;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies GraphML serialization for visible graph layouts.
/// </summary>
public sealed class KustoGraphMlSerializerTests
{
    /// <summary>
    /// Verifies that graph identities, metadata, directed endpoints, and positions survive serialization.
    /// </summary>
    [Fact]
    public void SerializePreservesVisibleGraphMetadataAndLayout()
    {
        DateTimeOffset firstDiscovered = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        DateTimeOffset lastUpdated = firstDiscovered.AddHours(2);
        GraphEntityKey source = new(GraphEntityKind.User, "User", "alice@example.com", "tenant-a");
        GraphEntityKey target = new(GraphEntityKind.Device, "Device", "device-42");
        GraphLayout layout = new(
            500,
            300,
            [
                new GraphLayoutNode(
                    new GraphEntitySummary(source, "Alice & Bob", firstDiscovered, lastUpdated, 7),
                    new GraphLayoutPoint(125.5, 60.25),
                    90,
                    42,
                    true),
                new GraphLayoutNode(
                    new GraphEntitySummary(target, "Device 42", firstDiscovered, lastUpdated, 3),
                    new GraphLayoutPoint(350, 210),
                    100,
                    44,
                    false),
            ],
            [
                new GraphLayoutEdge(
                    new GraphRelationshipKey(source, target, "AuthenticatedTo", "event-9"),
                    [
                        new GraphLayoutPoint(125.5, 60.25),
                        new GraphLayoutPoint(240, 120),
                        new GraphLayoutPoint(350, 210),
                    ]),
            ],
            true);

        byte[] content = KustoGraphMlSerializer.Serialize(layout);

        Assert.False(content.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        XDocument document = XDocument.Parse(Encoding.UTF8.GetString(content));
        XNamespace graphMl = "http://graphml.graphdrawing.org/xmlns";
        XElement graph = Assert.Single(document.Root!.Elements(graphMl + "graph"));
        Assert.Equal("directed", graph.Attribute("edgedefault")?.Value);
        Assert.Equal("true", GetData(graph, graphMl, "graph-truncated"));

        XElement[] nodes = graph.Elements(graphMl + "node").ToArray();
        Assert.Equal(2, nodes.Length);
        Assert.Equal("Alice & Bob", GetData(nodes[0], graphMl, "node-label"));
        Assert.Equal("User", GetData(nodes[0], graphMl, "node-kind"));
        Assert.Equal("alice@example.com", GetData(nodes[0], graphMl, "node-canonical-id"));
        Assert.Equal("tenant-a", GetData(nodes[0], graphMl, "node-source-namespace"));
        Assert.Equal("7", GetData(nodes[0], graphMl, "node-degree"));
        Assert.Equal("125.5", GetData(nodes[0], graphMl, "node-x"));
        Assert.Equal("60.25", GetData(nodes[0], graphMl, "node-y"));
        Assert.Equal("true", GetData(nodes[0], graphMl, "node-center"));

        XElement edge = Assert.Single(graph.Elements(graphMl + "edge"));
        Assert.Equal(nodes[0].Attribute("id")?.Value, edge.Attribute("source")?.Value);
        Assert.Equal(nodes[1].Attribute("id")?.Value, edge.Attribute("target")?.Value);
        Assert.Equal("AuthenticatedTo", GetData(edge, graphMl, "edge-type"));
        Assert.Equal("event-9", GetData(edge, graphMl, "edge-discriminator"));
        Assert.Equal("125.5,60.25 240,120 350,210", GetData(edge, graphMl, "edge-route"));
    }

    private static string GetData(XElement owner, XNamespace graphMl, string key)
    {
        return Assert.Single(
            owner.Elements(graphMl + "data"),
            element => element.Attribute("key")?.Value == key).Value;
    }
}
