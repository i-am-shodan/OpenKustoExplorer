using System.Globalization;
using System.Text;
using System.Xml;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Desktop.Graphs;

/// <summary>
/// Serializes a visible graph layout to interoperable GraphML.
/// </summary>
public static class KustoGraphMlSerializer
{
    private const string GraphMlNamespace = "http://graphml.graphdrawing.org/xmlns";

    /// <summary>
    /// Serializes the supplied graph layout as UTF-8 GraphML.
    /// </summary>
    /// <param name="layout">The visible graph layout to serialize.</param>
    /// <returns>The serialized GraphML document.</returns>
    public static byte[] Serialize(GraphLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        using MemoryStream stream = new();
        using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            NewLineChars = "\n",
        }))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("graphml", GraphMlNamespace);
            WriteKey(writer, "graph-truncated", "graph", "truncated", "boolean");
            WriteKey(writer, "node-label", "node", "label", "string");
            WriteKey(writer, "node-kind", "node", "entityKind", "string");
            WriteKey(writer, "node-type", "node", "entityType", "string");
            WriteKey(writer, "node-canonical-id", "node", "canonicalId", "string");
            WriteKey(writer, "node-source-namespace", "node", "sourceNamespace", "string");
            WriteKey(writer, "node-first-discovered", "node", "firstDiscoveredAtUtc", "string");
            WriteKey(writer, "node-last-updated", "node", "lastUpdatedAtUtc", "string");
            WriteKey(writer, "node-degree", "node", "degree", "long");
            WriteKey(writer, "node-x", "node", "x", "double");
            WriteKey(writer, "node-y", "node", "y", "double");
            WriteKey(writer, "node-width", "node", "width", "double");
            WriteKey(writer, "node-height", "node", "height", "double");
            WriteKey(writer, "node-center", "node", "isCenter", "boolean");
            WriteKey(writer, "edge-type", "edge", "relationshipType", "string");
            WriteKey(writer, "edge-discriminator", "edge", "discriminator", "string");
            WriteKey(writer, "edge-route", "edge", "route", "string");

            writer.WriteStartElement("graph", GraphMlNamespace);
            writer.WriteAttributeString("id", "G");
            writer.WriteAttributeString("edgedefault", "directed");
            WriteData(writer, "graph-truncated", XmlConvert.ToString(layout.IsTruncated));

            Dictionary<GraphEntityKey, string> nodeIds = new(layout.Nodes.Count);
            for (int index = 0; index < layout.Nodes.Count; index++)
            {
                GraphLayoutNode node = layout.Nodes[index];
                string nodeId = string.Create(CultureInfo.InvariantCulture, $"n{index}");
                nodeIds.Add(node.Entity.Entity, nodeId);
                WriteNode(writer, nodeId, node);
            }

            for (int index = 0; index < layout.Edges.Count; index++)
            {
                WriteEdge(writer, string.Create(CultureInfo.InvariantCulture, $"e{index}"), layout.Edges[index], nodeIds);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    private static void WriteKey(XmlWriter writer, string id, string scope, string name, string type)
    {
        writer.WriteStartElement("key", GraphMlNamespace);
        writer.WriteAttributeString("id", id);
        writer.WriteAttributeString("for", scope);
        writer.WriteAttributeString("attr.name", name);
        writer.WriteAttributeString("attr.type", type);
        writer.WriteEndElement();
    }

    private static void WriteNode(XmlWriter writer, string id, GraphLayoutNode node)
    {
        GraphEntitySummary summary = node.Entity;
        GraphEntityKey entity = summary.Entity;
        writer.WriteStartElement("node", GraphMlNamespace);
        writer.WriteAttributeString("id", id);
        WriteData(writer, "node-label", summary.DisplayLabel);
        WriteData(writer, "node-kind", entity.Kind.ToString());
        WriteData(writer, "node-type", entity.TypeName);
        WriteData(writer, "node-canonical-id", entity.CanonicalId);
        WriteData(writer, "node-source-namespace", entity.SourceNamespace);
        WriteData(writer, "node-first-discovered", summary.FirstDiscoveredAtUtc.ToString("O", CultureInfo.InvariantCulture));
        WriteData(writer, "node-last-updated", summary.LastUpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        WriteData(writer, "node-degree", XmlConvert.ToString(summary.Degree));
        WriteData(writer, "node-x", XmlConvert.ToString(node.Center.X));
        WriteData(writer, "node-y", XmlConvert.ToString(node.Center.Y));
        WriteData(writer, "node-width", XmlConvert.ToString(node.Width));
        WriteData(writer, "node-height", XmlConvert.ToString(node.Height));
        WriteData(writer, "node-center", XmlConvert.ToString(node.IsCenter));
        writer.WriteEndElement();
    }

    private static void WriteEdge(
        XmlWriter writer,
        string id,
        GraphLayoutEdge edge,
        Dictionary<GraphEntityKey, string> nodeIds)
    {
        writer.WriteStartElement("edge", GraphMlNamespace);
        writer.WriteAttributeString("id", id);
        writer.WriteAttributeString("source", nodeIds[edge.Relationship.Source]);
        writer.WriteAttributeString("target", nodeIds[edge.Relationship.Target]);
        WriteData(writer, "edge-type", edge.Relationship.TypeName);
        WriteData(writer, "edge-discriminator", edge.Relationship.Discriminator);
        WriteData(writer, "edge-route", string.Join(
            " ",
            edge.Route.Select(point => string.Create(CultureInfo.InvariantCulture, $"{point.X},{point.Y}"))));
        writer.WriteEndElement();
    }

    private static void WriteData(XmlWriter writer, string key, string value)
    {
        writer.WriteStartElement("data", GraphMlNamespace);
        writer.WriteAttributeString("key", key);
        writer.WriteString(value);
        writer.WriteEndElement();
    }
}
