using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Creates stable, type-aware identities for recorded Kusto values.
/// </summary>
public static class KustoRecordedValueCanonicalizer
{
    /// <summary>
    /// Creates an exact typed identity for one result value.
    /// </summary>
    /// <param name="typeName">The server-reported column type.</param>
    /// <param name="value">The recorded result value.</param>
    /// <returns>The canonical typed identity.</returns>
    public static KustoRecordedValueIdentity Create(string typeName, KustoResultValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(value);
        string normalizedType = NormalizeType(typeName);

        if (value.IsNull)
        {
            return new KustoRecordedValueIdentity(normalizedType, string.Empty, true);
        }

        string canonicalValue = normalizedType switch
        {
            "bool" => NormalizeBoolean(value),
            "int" or "long" => NormalizeInteger(value),
            "real" => NormalizeReal(value),
            "decimal" => NormalizeDecimal(value),
            "datetime" => NormalizeDateTime(value),
            "timespan" => NormalizeTimeSpan(value),
            "guid" => NormalizeGuid(value),
            "dynamic" => NormalizeDynamic(value),
            _ => ReadString(value),
        };
        return new KustoRecordedValueIdentity(normalizedType, canonicalValue, false);
    }

    /// <summary>
    /// Creates a stable hash for indexed identity lookup.
    /// </summary>
    /// <param name="identity">The typed identity.</param>
    /// <returns>A lowercase SHA-256 hexadecimal key.</returns>
    public static string CreateHash(KustoRecordedValueIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string payload = $"{identity.TypeName}\0{(identity.IsNull ? "1" : "0")}\0{identity.CanonicalValue}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }

    private static string NormalizeType(string typeName)
    {
        string normalized = typeName.Trim().ToLowerInvariant();
        return normalized switch
        {
            _ when normalized.Contains("bool", StringComparison.Ordinal) => "bool",
            _ when normalized.Contains("datetime", StringComparison.Ordinal) => "datetime",
            _ when normalized.Contains("decimal", StringComparison.Ordinal) => "decimal",
            _ when normalized.Contains("guid", StringComparison.Ordinal) => "guid",
            _ when normalized.Contains("int32", StringComparison.Ordinal) || normalized == "int" => "int",
            _ when normalized.Contains("int64", StringComparison.Ordinal) || normalized == "long" => "long",
            _ when normalized.Contains("double", StringComparison.Ordinal) || normalized.Contains("real", StringComparison.Ordinal) => "real",
            _ when normalized.Contains("timespan", StringComparison.Ordinal) => "timespan",
            _ when normalized.Contains("dynamic", StringComparison.Ordinal) || normalized.Contains("object", StringComparison.Ordinal) => "dynamic",
            _ => "string",
        };
    }

    private static string NormalizeBoolean(KustoResultValue value)
    {
        return bool.TryParse(ReadScalarText(value), out bool parsed)
            ? parsed.ToString().ToLowerInvariant()
            : value.DisplayText;
    }

    private static string NormalizeInteger(KustoResultValue value)
    {
        return long.TryParse(ReadScalarText(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : value.DisplayText;
    }

    private static string NormalizeReal(KustoResultValue value)
    {
        return double.TryParse(ReadScalarText(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed.ToString("R", CultureInfo.InvariantCulture)
            : value.DisplayText;
    }

    private static string NormalizeDecimal(KustoResultValue value)
    {
        return decimal.TryParse(ReadScalarText(value), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : value.DisplayText;
    }

    private static string NormalizeDateTime(KustoResultValue value)
    {
        return DateTimeOffset.TryParse(
            ReadScalarText(value),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed)
            ? parsed.ToString("O", CultureInfo.InvariantCulture)
            : value.DisplayText;
    }

    private static string NormalizeTimeSpan(KustoResultValue value)
    {
        return TimeSpan.TryParse(ReadScalarText(value), CultureInfo.InvariantCulture, out TimeSpan parsed)
            ? parsed.ToString("c", CultureInfo.InvariantCulture)
            : value.DisplayText;
    }

    private static string NormalizeGuid(KustoResultValue value)
    {
        return Guid.TryParse(ReadScalarText(value), out Guid parsed)
            ? parsed.ToString("D")
            : value.DisplayText;
    }

    private static string NormalizeDynamic(KustoResultValue value)
    {
        string source = value.RawJson ?? value.DisplayText;

        try
        {
            using JsonDocument document = JsonDocument.Parse(source);
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream))
            {
                WriteCanonicalJson(writer, document.RootElement);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return value.DisplayText;
        }
    }

    private static string ReadString(KustoResultValue value)
    {
        if (value.RawJson is not null)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(value.RawJson);
                if (document.RootElement.ValueKind == JsonValueKind.String)
                {
                    return document.RootElement.GetString() ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // Legacy display-only values use their invariant text.
            }
        }

        return value.DisplayText;
    }

    private static string ReadScalarText(KustoResultValue value)
    {
        if (value.RawJson is not null)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(value.RawJson);
                return document.RootElement.ValueKind == JsonValueKind.String
                    ? document.RootElement.GetString() ?? string.Empty
                    : document.RootElement.GetRawText();
            }
            catch (JsonException)
            {
                // Legacy display-only values use their invariant text.
            }
        }

        return value.DisplayText;
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element
                    .EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
