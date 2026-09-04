using System.Text.Json;
using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Infrastructure.Connections;

/// <summary>
/// Parses the JSON payload returned by the Kusto database schema management command.
/// </summary>
internal static class KustoDatabaseSchemaParser
{
    /// <summary>
    /// Parses one database schema payload.
    /// </summary>
    /// <param name="clusterName">The cluster host name.</param>
    /// <param name="databaseName">The requested database name.</param>
    /// <param name="json">The schema JSON returned by Kusto.</param>
    /// <returns>The immutable database schema.</returns>
    public static KustoDatabaseSchema Parse(string clusterName, string databaseName, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement databases = document.RootElement.GetProperty("Databases");
        JsonElement database = GetDatabase(databases, databaseName);
        List<KustoTableSchema> tables = [];

        if (database.TryGetProperty("Tables", out JsonElement tableMap))
        {
            foreach (JsonProperty tableProperty in tableMap.EnumerateObject())
            {
                tables.Add(ParseTable(tableProperty.Name, tableProperty.Value));
            }
        }

        return new KustoDatabaseSchema(clusterName, databaseName, tables);
    }

    private static JsonElement GetDatabase(JsonElement databases, string databaseName)
    {
        JsonElement database = default;
        bool found = databases.TryGetProperty(databaseName, out database);

        if (!found)
        {
            foreach (JsonProperty databaseProperty in databases
                .EnumerateObject()
                .Where(property => string.Equals(property.Name, databaseName, StringComparison.OrdinalIgnoreCase)))
            {
                database = databaseProperty.Value;
                found = true;
            }
        }

        if (!found)
        {
            throw new InvalidDataException($"Kusto returned no schema for database {databaseName}.");
        }

        return database;
    }

    private static KustoTableSchema ParseTable(string fallbackName, JsonElement tableElement)
    {
        string tableName = GetOptionalString(tableElement, "Name") ?? fallbackName;
        List<KustoColumnSchema> columns = [];

        if (tableElement.TryGetProperty("OrderedColumns", out JsonElement orderedColumns))
        {
            foreach (JsonElement columnElement in orderedColumns.EnumerateArray())
            {
                string columnName = GetOptionalString(columnElement, "Name")
                    ?? throw new InvalidDataException($"Table {tableName} contains a column without a name.");
                string typeName = GetOptionalString(columnElement, "CslType")
                    ?? GetOptionalString(columnElement, "Type")
                    ?? "dynamic";
                columns.Add(new KustoColumnSchema(columnName, ParseScalarType(typeName)));
            }
        }

        return new KustoTableSchema(tableName, columns);
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        string? value = null;
        if (element.TryGetProperty(propertyName, out JsonElement property))
        {
            value = property.GetString();
        }

        return value;
    }

    private static KustoScalarType ParseScalarType(string typeName)
    {
        string normalizedType = typeName.ToLowerInvariant();
        KustoScalarType scalarType = normalizedType switch
        {
            "bool" or "boolean" or "system.boolean" => KustoScalarType.Bool,
            "datetime" or "system.datetime" => KustoScalarType.DateTime,
            "decimal" or "system.decimal" or "system.data.sqltypes.sqldecimal" => KustoScalarType.FixedPoint,
            "dynamic" or "system.object" => KustoScalarType.Dynamic,
            "guid" or "system.guid" => KustoScalarType.Identifier,
            "int" or "int32" or "system.int32" => KustoScalarType.WholeNumber,
            "long" or "int64" or "system.int64" => KustoScalarType.WideInteger,
            "real" or "double" or "system.double" or "system.single" => KustoScalarType.Real,
            "string" or "system.string" => KustoScalarType.Text,
            "timespan" or "time" or "system.timespan" => KustoScalarType.TimeSpan,
            _ => KustoScalarType.Dynamic,
        };

        return scalarType;
    }
}
