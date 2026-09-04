using System.Text;
using System.Text.Json;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Encodes graph collections as deterministic JSON without reflection-based serialization.
/// </summary>
internal static class GraphSqliteJson
{
    /// <summary>
    /// Reads retained graph properties from a compact JSON object.
    /// </summary>
    /// <param name="json">The compact JSON object.</param>
    /// <returns>The retained graph properties in source-name order.</returns>
    internal static IReadOnlyList<GraphEntityProperty> ReadProperties(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Graph entity properties must be a JSON object.");
        }

        return document.RootElement
            .EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new GraphEntityProperty(
                property.Name,
                property.Value.GetString() ?? string.Empty))
            .ToArray();
    }

    /// <summary>
    /// Reads graph labels from a compact JSON array.
    /// </summary>
    /// <param name="json">The compact JSON array.</param>
    /// <returns>The retained labels.</returns>
    internal static IReadOnlyList<string> ReadStrings(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Graph labels must be a JSON array.");
        }

        return document.RootElement
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
    }

    /// <summary>
    /// Writes an ordered graph property object.
    /// </summary>
    /// <param name="properties">The graph properties.</param>
    /// <returns>The compact UTF-8 JSON object.</returns>
    internal static string WriteProperties(IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();

            foreach (KeyValuePair<string, string> property in properties.OrderBy(
                property => property.Key,
                StringComparer.Ordinal))
            {
                writer.WriteString(property.Key, property.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Writes graph labels or evidence identifiers as an ordered JSON array.
    /// </summary>
    /// <param name="values">The string values.</param>
    /// <returns>The compact UTF-8 JSON array.</returns>
    internal static string WriteStrings(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartArray();

            foreach (string value in values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
