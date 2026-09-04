using System.Collections.ObjectModel;
using System.Text.Json;

namespace OpenKustoExplorer.Infrastructure.Storage;

/// <summary>
/// Reads optional and required scalar values from parsed JSON elements for the persisted catalogs.
/// </summary>
internal static class JsonElementReader
{
    /// <summary>
    /// Reads a required non-empty string property.
    /// </summary>
    /// <param name="element">The owning JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The property value.</returns>
    /// <exception cref="InvalidDataException">The property is missing, empty, or not a string.</exception>
    internal static string GetRequiredString(JsonElement element, string propertyName)
    {
        return GetOptionalString(element, propertyName)
            ?? throw new InvalidDataException($"The required property '{propertyName}' is missing.");
    }

    /// <summary>
    /// Reads an optional non-empty string property.
    /// </summary>
    /// <param name="element">The owning JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The property value, or <see langword="null"/> when absent or empty.</returns>
    internal static string? GetOptionalString(JsonElement element, string propertyName)
    {
        string? value = null;

        if (element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Reads an optional boolean property.
    /// </summary>
    /// <param name="element">The owning JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="defaultValue">The value returned when the property is absent.</param>
    /// <returns>The property value, or <paramref name="defaultValue"/> when absent.</returns>
    internal static bool GetOptionalBoolean(JsonElement element, string propertyName, bool defaultValue)
    {
        bool value = defaultValue;

        if (element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
        }

        return value;
    }

    /// <summary>
    /// Reads an optional 32-bit integer property.
    /// </summary>
    /// <param name="element">The owning JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="defaultValue">The value returned when the property is absent.</param>
    /// <returns>The property value, or <paramref name="defaultValue"/> when absent.</returns>
    internal static int GetOptionalInt32(JsonElement element, string propertyName, int defaultValue)
    {
        int value = defaultValue;

        if (element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number)
        {
            value = property.GetInt32();
        }

        return value;
    }

    /// <summary>
    /// Reads an optional double property.
    /// </summary>
    /// <param name="element">The owning JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The property value, or <see langword="null"/> when absent.</returns>
    internal static double? GetOptionalDouble(JsonElement element, string propertyName)
    {
        double? value = null;

        if (element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number)
        {
            value = property.GetDouble();
        }

        return value;
    }

    /// <summary>
    /// Reads an optional date-time-offset property serialized as a string.
    /// </summary>
    /// <param name="element">The owning JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The property value, or <see langword="null"/> when absent.</returns>
    internal static DateTimeOffset? GetOptionalDateTimeOffset(JsonElement element, string propertyName)
    {
        DateTimeOffset? value = null;

        if (element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetDateTimeOffset();
        }

        return value;
    }

    /// <summary>
    /// Reads an optional array of non-null strings.
    /// </summary>
    /// <param name="element">The owning JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The string values in document order.</returns>
    internal static ReadOnlyCollection<string> ReadStringArray(JsonElement element, string propertyName)
    {
        string[] values = [];

        if (element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.Array)
        {
            values = property
                .EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString())
                .OfType<string>()
                .ToArray();
        }

        return Array.AsReadOnly(values);
    }
}
