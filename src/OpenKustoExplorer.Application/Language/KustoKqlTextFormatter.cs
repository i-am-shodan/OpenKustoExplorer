using System.Globalization;
using System.Text.Json;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Formats identifiers, scalar types, and values as safe KQL source text.
/// </summary>
public static class KustoKqlTextFormatter
{
    /// <summary>
    /// Escapes a KQL identifier using bracket notation.
    /// </summary>
    /// <param name="name">The identifier name.</param>
    /// <returns>The escaped identifier.</returns>
    public static string EscapeIdentifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"['{name.Replace("'", "''", StringComparison.Ordinal)}']";
    }

    /// <summary>
    /// Formats a simple identifier without brackets when safe.
    /// </summary>
    /// <param name="name">The identifier name.</param>
    /// <returns>The formatted identifier.</returns>
    public static string FormatIdentifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        bool isSimple = name[0] is '_' || char.IsLetter(name[0]);
        for (int index = 1; index < name.Length && isSimple; index++)
        {
            isSimple = name[index] is '_' || char.IsLetterOrDigit(name[index]);
        }

        return isSimple ? name : EscapeIdentifier(name);
    }

    /// <summary>
    /// Normalizes a server-reported type to a Kusto scalar type name.
    /// </summary>
    /// <param name="typeName">The server-reported type.</param>
    /// <returns>The normalized Kusto scalar type.</returns>
    public static string NormalizeType(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        string normalizedType = typeName.ToLowerInvariant();
        return normalizedType switch
        {
            _ when normalizedType.Contains("bool", StringComparison.Ordinal) => "bool",
            _ when normalizedType.Contains("datetime", StringComparison.Ordinal) => "datetime",
            _ when normalizedType.Contains("decimal", StringComparison.Ordinal) => "decimal",
            _ when normalizedType.Contains("guid", StringComparison.Ordinal) => "guid",
            _ when normalizedType.Contains("int32", StringComparison.Ordinal) || normalizedType == "int" => "int",
            _ when normalizedType.Contains("int64", StringComparison.Ordinal) || normalizedType == "long" => "long",
            _ when normalizedType.Contains("double", StringComparison.Ordinal) || normalizedType.Contains("real", StringComparison.Ordinal) => "real",
            _ when normalizedType.Contains("timespan", StringComparison.Ordinal) => "timespan",
            _ when normalizedType.Contains("dynamic", StringComparison.Ordinal) || normalizedType.Contains("object", StringComparison.Ordinal) => "dynamic",
            _ => "string",
        };
    }

    /// <summary>
    /// Formats an exact typed identity as a KQL scalar literal.
    /// </summary>
    /// <param name="identity">The typed identity.</param>
    /// <returns>The KQL literal.</returns>
    public static string FormatLiteral(KustoRecordedValueIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return FormatLiteral(identity.CanonicalValue, identity.TypeName, identity.IsNull);
    }

    /// <summary>
    /// Formats a scalar value as a KQL literal.
    /// </summary>
    /// <param name="value">The invariant scalar value.</param>
    /// <param name="typeName">The server-reported or normalized Kusto type.</param>
    /// <param name="isNull">Whether the value is null.</param>
    /// <returns>The KQL literal.</returns>
    public static string FormatLiteral(string value, string typeName, bool isNull = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        string kustoType = NormalizeType(typeName);
        if (isNull)
        {
            return $"{kustoType}(null)";
        }

        string escapedValue = value.Replace("'", "''", StringComparison.Ordinal);
        return kustoType switch
        {
            "bool" => bool.TryParse(value, out bool parsedBoolean)
                ? parsedBoolean.ToString().ToLowerInvariant()
                : "bool(null)",
            "int" or "long" => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                ? value
                : $"{kustoType}(null)",
            "real" or "decimal" => double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsedNumber) && double.IsFinite(parsedNumber)
                    ? value
                    : $"{kustoType}(null)",
            "datetime" => $"datetime('{escapedValue}')",
            "timespan" => $"time('{escapedValue}')",
            "guid" => $"guid('{escapedValue}')",
            "dynamic" => IsWellFormedJson(value)
                ? $"dynamic({value})"
                : $"dynamic('{escapedValue}')",
            _ => $"'{escapedValue}'",
        };
    }

    private static bool IsWellFormedJson(string value)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind is not JsonValueKind.Undefined;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
