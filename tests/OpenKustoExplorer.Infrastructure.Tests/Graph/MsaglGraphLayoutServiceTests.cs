using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Portable.Graphs;

namespace OpenKustoExplorer.Infrastructure.Tests.Graph;

/// <summary>
/// Verifies MSAGL layout is projected into dependency-neutral render geometry.
/// </summary>
public sealed class MsaglGraphLayoutServiceTests
{
    /// <summary>
    /// Verifies connected nodes receive distinct positions and a routed directed edge.
    /// </summary>
    /// <returns>A task that completes when layout finishes.</returns>
    [Fact]
    public async Task LayoutPositionsConnectedViewport()
    {
        DateTimeOffset discoveredAt = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        GraphEntityKey source = new(GraphEntityKind.User, "User", "alice");
        GraphEntityKey target = new(GraphEntityKind.Host, "Host", "server-1");
        GraphEntitySummary sourceSummary = new(source, "Alice", discoveredAt, discoveredAt, 1);
        GraphEntitySummary targetSummary = new(target, "Server 1", discoveredAt, discoveredAt, 1);
        GraphRelationshipKey relationship = new(source, target, "AuthenticatedTo");
        GraphViewport viewport = new(source, [sourceSummary, targetSummary], [relationship], false);
        MsaglGraphLayoutService service = new();

        GraphLayout layout = await service.LayoutAsync(viewport);

        Assert.Equal(2, layout.Nodes.Count);
        Assert.Single(layout.Edges);
        Assert.True(layout.Width > 0);
        Assert.True(layout.Height > 0);
        Assert.NotEqual(layout.Nodes[0].Center, layout.Nodes[1].Center);
        Assert.All(layout.Nodes, node =>
        {
            Assert.InRange(node.Center.X, 0, layout.Width);
            Assert.InRange(node.Center.Y, 0, layout.Height);
        });
        Assert.True(Assert.Single(layout.Edges).Route.Count >= 2);
        Assert.True(Assert.Single(layout.Nodes, node => node.IsCenter).Entity.Entity == source);
    }

    /// <summary>
    /// Verifies an elongated investigation chain is oriented across both canvas dimensions.
    /// </summary>
    /// <returns>A task that completes when layout finishes.</returns>
    [Fact]
    public async Task LayoutBalancesElongatedViewport()
    {
        const int NodeCount = 24;
        DateTimeOffset discoveredAt = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        List<GraphEntitySummary> entities = [];
        List<GraphRelationshipKey> relationships = [];

        for (int index = 0; index < NodeCount; index++)
        {
            GraphEntityKey entity = new(GraphEntityKind.Host, "Host", $"server-{index:N0}");
            entities.Add(new GraphEntitySummary(
                entity,
                $"Server {index:N0}",
                discoveredAt,
                discoveredAt,
                index is 0 or NodeCount - 1 ? 1 : 2));

            if (index > 0)
            {
                relationships.Add(new GraphRelationshipKey(
                    entities[index - 1].Entity,
                    entity,
                    "ConnectedTo"));
            }
        }

        GraphViewport viewport = new(entities[0].Entity, entities, relationships, false);
        MsaglGraphLayoutService service = new();

        GraphLayout layout = await service.LayoutAsync(viewport);
        double aspectRatio = layout.Width / layout.Height;

        Assert.InRange(aspectRatio, 0.75, 2.5);
        Assert.Equal(NodeCount, layout.Nodes.Count);
        Assert.Equal(NodeCount - 1, layout.Edges.Count);
        Assert.All(layout.Edges, edge => Assert.True(edge.Route.Count >= 2));

        for (int firstIndex = 0; firstIndex < layout.Nodes.Count; firstIndex++)
        {
            GraphLayoutNode first = layout.Nodes[firstIndex];

            for (int secondIndex = firstIndex + 1; secondIndex < layout.Nodes.Count; secondIndex++)
            {
                GraphLayoutNode second = layout.Nodes[secondIndex];
                bool overlaps = Math.Abs(first.Center.X - second.Center.X) < (first.Width + second.Width) / 2
                    && Math.Abs(first.Center.Y - second.Center.Y) < (first.Height + second.Height) / 2;
                Assert.False(overlaps, $"Layout nodes '{first.Entity.DisplayLabel}' and '{second.Entity.DisplayLabel}' overlap.");
            }
        }
    }
}
