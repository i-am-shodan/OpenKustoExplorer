using System.Buffers;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;

namespace OpenKustoExplorer.Infrastructure.Assistance;

/// <summary>
/// Creates bounded read-only Copilot tools pinned to one immutable graph generation.
/// </summary>
internal static class CopilotGraphTools
{
    /// <summary>
    /// Gets the explicit entity-detail tool name.
    /// </summary>
    internal const string GetEntityDetailsToolName = "get_graph_entity_details";

    /// <summary>
    /// Gets the explicit graph-schema tool name.
    /// </summary>
    internal const string GetSchemaToolName = "get_graph_schema";

    /// <summary>
    /// Gets the explicit read-only openCypher tool name.
    /// </summary>
    internal const string QueryToolName = "query_graph_opencypher";

    /// <summary>
    /// Gets the explicit shortest-route lookup tool name.
    /// </summary>
    internal const string RouteToolName = "find_graph_routes";
    private const int MaximumOutputBytes = 64 * 1024;
    private const int MaximumPropertyCount = 100;
    private const int MaximumValueLength = 500;

    /// <summary>
    /// Creates the exact graph tools available to one consented Graph conversation.
    /// </summary>
    /// <param name="graphStore">The durable graph detail store.</param>
    /// <param name="graphQueryService">The read-only graph query service.</param>
    /// <param name="snapshot">The graph generation captured for the conversation.</param>
    /// <returns>Four AOT-safe read-only tool declarations.</returns>
    internal static IReadOnlyList<AIFunction> Create(
        IGraphStore graphStore,
        IGraphQueryService graphQueryService,
        GraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(graphStore);
        ArgumentNullException.ThrowIfNull(graphQueryService);
        return Array.AsReadOnly<AIFunction>(
        [
            CopilotTool.DefineTool(
                async (CancellationToken cancellationToken) =>
                {
                    GraphQuerySchema schema = await graphQueryService.GetSchemaAsync(
                        snapshot,
                        cancellationToken).ConfigureAwait(false);
                    return SerializeSchema(schema);
                },
                factoryOptions: new AIFunctionFactoryOptions
                {
                    Name = GetSchemaToolName,
                    Description = "Gets bounded node labels and relationship types for the selected saved graph.",
                }),
            CopilotTool.DefineTool(
                async (
                    [Description("A read-only openCypher MATCH query. Write clauses are rejected.")] string query,
                    CancellationToken cancellationToken) =>
                {
                    GraphQueryRequest request = new(snapshot, query, 200, 200, 400);
                    GraphQueryResult result = await graphQueryService.ExecuteOpenCypherAsync(
                        request,
                        cancellationToken).ConfigureAwait(false);
                    return SerializeQueryResult(result);
                },
                factoryOptions: new AIFunctionFactoryOptions
                {
                    Name = QueryToolName,
                    Description = "Runs bounded read-only openCypher against the selected saved graph generation.",
                }),
            CopilotTool.DefineTool(
                async (
                    [Description("Graph entity kind, for example User or Host.")] string kind,
                    [Description("Exact graph entity type name.")] string typeName,
                    [Description("Exact canonical entity identifier.")] string canonicalId,
                    [Description("Exact source namespace, or an empty string for a global identity.")] string sourceNamespace,
                    CancellationToken cancellationToken) =>
                {
                    return await GetEntityDetailsAsync(
                        graphStore,
                        snapshot,
                        kind,
                        typeName,
                        canonicalId,
                        sourceNamespace,
                        cancellationToken).ConfigureAwait(false);
                },
                factoryOptions: new AIFunctionFactoryOptions
                {
                    Name = GetEntityDetailsToolName,
                    Description = "Gets bounded latest properties for one explicitly identified entity in the selected graph.",
                }),
            CopilotTool.DefineTool(
                async (
                    [Description("Start graph entity kind, for example User or Host.")] string startKind,
                    [Description("Exact start graph entity type name.")] string startTypeName,
                    [Description("Exact start canonical entity identifier.")] string startCanonicalId,
                    [Description("Exact start source namespace, or an empty string for a global identity.")] string startSourceNamespace,
                    [Description("Destination graph entity kind, for example User or Host.")] string destinationKind,
                    [Description("Exact destination graph entity type name.")] string destinationTypeName,
                    [Description("Exact destination canonical entity identifier.")] string destinationCanonicalId,
                    [Description("Exact destination source namespace, or an empty string for a global identity.")] string destinationSourceNamespace,
                    CancellationToken cancellationToken) =>
                {
                    GraphEntityKey? routeStart = CreateEntityKey(
                        startKind,
                        startTypeName,
                        startCanonicalId,
                        startSourceNamespace);
                    GraphEntityKey? routeDestination = CreateEntityKey(
                        destinationKind,
                        destinationTypeName,
                        destinationCanonicalId,
                        destinationSourceNamespace);

                    if (routeStart is not GraphEntityKey start
                        || routeDestination is not GraphEntityKey destination)
                    {
                        return "{\"error\":\"One or both graph route endpoints are invalid.\"}";
                    }

                    GraphRouteResult result = await graphStore.FindRoutesAsync(
                        snapshot,
                        start,
                        destination,
                        200,
                        400,
                        cancellationToken).ConfigureAwait(false);
                    return SerializeRouteResult(snapshot, result);
                },
                factoryOptions: new AIFunctionFactoryOptions
                {
                    Name = RouteToolName,
                    Description = "Finds bounded shortest undirected routes between two exact entities in the selected saved graph generation.",
                }),
        ]);
    }

    private static GraphEntityKey? CreateEntityKey(
        string kind,
        string typeName,
        string canonicalId,
        string sourceNamespace)
    {
        if (!Enum.TryParse(kind, ignoreCase: true, out GraphEntityKind entityKind)
            || !Enum.IsDefined(entityKind)
            || string.IsNullOrWhiteSpace(typeName)
            || string.IsNullOrWhiteSpace(canonicalId)
            || sourceNamespace is null)
        {
            return null;
        }

        return new GraphEntityKey(entityKind, typeName, canonicalId, sourceNamespace);
    }

    private static async Task<string> GetEntityDetailsAsync(
        IGraphStore graphStore,
        GraphSnapshot snapshot,
        string kind,
        string typeName,
        string canonicalId,
        string sourceNamespace,
        CancellationToken cancellationToken)
    {
        string json;

        if (!Enum.TryParse(kind, ignoreCase: true, out GraphEntityKind entityKind)
            || !Enum.IsDefined(entityKind))
        {
            json = "{\"error\":\"Unknown graph entity kind.\"}";
        }
        else
        {
            GraphEntityKey key = new(entityKind, typeName, canonicalId, sourceNamespace);
            GraphEntityDetails? details = await graphStore.GetEntityDetailsAsync(
                snapshot,
                key,
                cancellationToken).ConfigureAwait(false);
            json = SerializeEntityDetails(details);
        }

        return json;
    }

    private static string SerializeSchema(GraphQuerySchema schema)
    {
        return Serialize(writer =>
        {
            writer.WriteStartObject();
            WriteSnapshot(writer, schema.Snapshot);
            writer.WritePropertyName("nodeLabels");
            WriteSchemaEntries(writer, schema.NodeLabels);
            writer.WritePropertyName("relationshipTypes");
            WriteSchemaEntries(writer, schema.RelationshipTypes);
            writer.WriteEndObject();
        });
    }

    private static string SerializeQueryResult(GraphQueryResult result)
    {
        return Serialize(writer =>
        {
            writer.WriteStartObject();
            WriteSnapshot(writer, result.Snapshot);
            writer.WriteBoolean("succeeded", result.Succeeded);
            writer.WriteBoolean("rowsTruncated", result.AreRowsTruncated);
            writer.WriteBoolean("viewportTruncated", result.Viewport.IsTruncated);
            writer.WriteNumber("durationMilliseconds", result.Duration.TotalMilliseconds);
            WriteDiagnostics(writer, result.Diagnostics);
            WriteColumns(writer, result.Columns);
            WriteRows(writer, result.Rows);
            WriteViewport(writer, result.Viewport);
            writer.WriteEndObject();
        });
    }

    private static string SerializeEntityDetails(GraphEntityDetails? details)
    {
        return Serialize(writer =>
        {
            writer.WriteStartObject();

            if (details is null)
            {
                writer.WriteNull("entity");
            }
            else
            {
                writer.WritePropertyName("entity");
                WriteEntitySummary(writer, details.Summary);
                writer.WriteNumber("observationCount", details.ObservationCount);
                writer.WriteNumber("evidenceCount", details.EvidenceCount);
                writer.WritePropertyName("sourceLabels");
                writer.WriteStartArray();

                foreach (string label in details.SourceLabels.Take(MaximumPropertyCount))
                {
                    writer.WriteStringValue(Truncate(label));
                }

                writer.WriteEndArray();
                writer.WritePropertyName("properties");
                writer.WriteStartObject();

                foreach (GraphEntityProperty property in details.Properties.Take(MaximumPropertyCount))
                {
                    writer.WriteString(property.Name, Truncate(property.Value));
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        });
    }

    private static string SerializeRouteResult(GraphSnapshot snapshot, GraphRouteResult result)
    {
        return Serialize(writer =>
        {
            writer.WriteStartObject();
            WriteSnapshot(writer, snapshot);
            writer.WriteBoolean("connected", result.IsConnected);
            writer.WriteBoolean("viewportTruncated", result.Viewport.IsTruncated);

            if (result.ShortestHopCount is int shortestHopCount)
            {
                writer.WriteNumber("shortestHopCount", shortestHopCount);
            }
            else
            {
                writer.WriteNull("shortestHopCount");
            }

            WriteViewport(writer, result.Viewport);
            writer.WriteEndObject();
        });
    }

    private static string Serialize(Action<Utf8JsonWriter> writeAction)
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            writeAction(writer);
        }

        return buffer.WrittenCount <= MaximumOutputBytes
            ? Encoding.UTF8.GetString(buffer.WrittenSpan)
            : "{\"truncated\":true,\"message\":\"Graph tool output exceeded 64 KiB. Use a narrower query.\"}";
    }

    private static string Truncate(string value)
    {
        return value.Length <= MaximumValueLength ? value : value[..MaximumValueLength];
    }

    private static void WriteSnapshot(Utf8JsonWriter writer, GraphSnapshot snapshot)
    {
        writer.WriteString("graphId", snapshot.GraphId);
        writer.WriteString("generationId", snapshot.GenerationId);
    }

    private static void WriteSchemaEntries(
        Utf8JsonWriter writer,
        IReadOnlyList<GraphQuerySchemaEntry> entries)
    {
        writer.WriteStartArray();

        foreach (GraphQuerySchemaEntry entry in entries.Take(100))
        {
            writer.WriteStartObject();
            writer.WriteString("name", entry.Name);
            writer.WriteNumber("count", entry.Count);
            writer.WritePropertyName("properties");
            writer.WriteStartArray();

            foreach (string propertyName in entry.PropertyNames.Take(MaximumPropertyCount))
            {
                writer.WriteStringValue(propertyName);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteDiagnostics(
        Utf8JsonWriter writer,
        IReadOnlyList<GraphQueryDiagnostic> diagnostics)
    {
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();

        foreach (GraphQueryDiagnostic diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("severity", diagnostic.Severity.ToString());
            writer.WriteNumber("start", diagnostic.Start);
            writer.WriteNumber("length", diagnostic.Length);
            writer.WriteString("message", Truncate(diagnostic.Message));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteColumns(
        Utf8JsonWriter writer,
        IReadOnlyList<GraphQueryColumn> columns)
    {
        writer.WritePropertyName("columns");
        writer.WriteStartArray();

        foreach (GraphQueryColumn column in columns)
        {
            writer.WriteStartObject();
            writer.WriteString("name", column.Name);
            writer.WriteString("kind", column.Kind.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRows(Utf8JsonWriter writer, IReadOnlyList<GraphQueryRow> rows)
    {
        writer.WritePropertyName("rows");
        writer.WriteStartArray();

        foreach (GraphQueryRow row in rows.Take(200))
        {
            writer.WriteStartArray();

            foreach (GraphQueryValue value in row.Values)
            {
                WriteQueryValue(writer, value);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private static void WriteQueryValue(Utf8JsonWriter writer, GraphQueryValue value)
    {
        if (value.Kind == GraphQueryValueKind.Null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteString("kind", value.Kind.ToString());
            writer.WriteString("value", Truncate(value.DisplayText));
            writer.WriteEndObject();
        }
    }

    private static void WriteViewport(Utf8JsonWriter writer, GraphViewport viewport)
    {
        writer.WritePropertyName("nodes");
        writer.WriteStartArray();

        foreach (GraphEntitySummary entity in viewport.Entities.Take(200))
        {
            WriteEntitySummary(writer, entity);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("relationships");
        writer.WriteStartArray();

        foreach (GraphRelationshipKey relationship in viewport.Relationships.Take(400))
        {
            writer.WriteStartObject();
            writer.WriteString("source", relationship.Source.ToString());
            writer.WriteString("target", relationship.Target.ToString());
            writer.WriteString("type", relationship.TypeName);
            writer.WriteString("discriminator", relationship.Discriminator);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteEntitySummary(Utf8JsonWriter writer, GraphEntitySummary entity)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", entity.Entity.Kind.ToString());
        writer.WriteString("typeName", entity.Entity.TypeName);
        writer.WriteString("canonicalId", entity.Entity.CanonicalId);
        writer.WriteString("sourceNamespace", entity.Entity.SourceNamespace);
        writer.WriteString("displayLabel", Truncate(entity.DisplayLabel));
        writer.WriteNumber("degree", entity.Degree);
        writer.WriteEndObject();
    }
}
