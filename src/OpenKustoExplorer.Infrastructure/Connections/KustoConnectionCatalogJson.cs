using System.Text.Json;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Domain.Schema;
using static OpenKustoExplorer.Infrastructure.Storage.JsonElementReader;

namespace OpenKustoExplorer.Infrastructure.Connections;

/// <summary>
/// Reads and writes the connection catalog with explicit AOT-safe JSON handling.
/// </summary>
internal static class KustoConnectionCatalogJson
{
    private const int CurrentVersion = 1;

    /// <summary>
    /// Reads a connection catalog from JSON.
    /// </summary>
    /// <param name="stream">The readable UTF-8 JSON stream.</param>
    /// <returns>The immutable connection catalog.</returns>
    public static KustoConnectionCatalog Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        int version = root.GetProperty("version").GetInt32();
        if (version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported connection catalog version {version}.");
        }

        List<KustoClusterConnection> clusters = [];
        foreach (JsonElement clusterElement in root.GetProperty("clusters").EnumerateArray())
        {
            clusters.Add(ReadCluster(clusterElement));
        }

        return new KustoConnectionCatalog(clusters);
    }

    /// <summary>
    /// Writes a connection catalog as UTF-8 JSON.
    /// </summary>
    /// <param name="stream">The writable destination stream.</param>
    /// <param name="catalog">The immutable connection catalog.</param>
    public static void Write(Stream stream, KustoConnectionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(catalog);

        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", CurrentVersion);
        writer.WritePropertyName("clusters");
        writer.WriteStartArray();

        foreach (KustoClusterConnection cluster in catalog.Clusters)
        {
            WriteCluster(writer, cluster);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static KustoClusterConnection ReadCluster(JsonElement element)
    {
        Uri clusterUri = new(GetRequiredString(element, "uri"), UriKind.Absolute);
        string displayName = GetRequiredString(element, "displayName");
        string? folderName = GetOptionalString(element, "folderName");
        List<KustoDatabaseConnection> databases = [];

        foreach (JsonElement databaseElement in element.GetProperty("databases").EnumerateArray())
        {
            databases.Add(ReadDatabase(clusterUri, databaseElement));
        }

        return new KustoClusterConnection(clusterUri, displayName, databases, folderName);
    }

    private static KustoDatabaseConnection ReadDatabase(Uri clusterUri, JsonElement element)
    {
        string name = GetRequiredString(element, "name");
        string displayName = GetRequiredString(element, "displayName");
        KustoDatabaseSchema? schema = null;

        bool hasTables = element.TryGetProperty("tables", out JsonElement tablesElement);
        bool hasFunctions = element.TryGetProperty("functions", out JsonElement functionsElement);
        if (hasTables && hasFunctions)
        {
            List<KustoTableSchema> tables = [];
            if (hasTables)
            {
                foreach (JsonElement tableElement in tablesElement.EnumerateArray())
                {
                    tables.Add(ReadTable(tableElement));
                }
            }

            List<KustoFunctionSchema> functions = [];
            if (hasFunctions)
            {
                foreach (JsonElement functionElement in functionsElement.EnumerateArray())
                {
                    functions.Add(ReadFunction(functionElement));
                }
            }

            schema = new KustoDatabaseSchema(clusterUri.Host, name, tables, functions);
        }

        return new KustoDatabaseConnection(name, displayName, schema);
    }

    private static KustoTableSchema ReadTable(JsonElement element)
    {
        string name = GetRequiredString(element, "name");
        List<KustoColumnSchema> columns = [];

        foreach (JsonElement columnElement in element.GetProperty("columns").EnumerateArray())
        {
            string columnName = GetRequiredString(columnElement, "name");
            string typeName = GetRequiredString(columnElement, "type");
            columns.Add(new KustoColumnSchema(columnName, ParseScalarType(typeName)));
        }

        return new KustoTableSchema(name, columns);
    }

    private static KustoFunctionSchema ReadFunction(JsonElement element)
    {
        return new KustoFunctionSchema(
            GetRequiredString(element, "name"),
            GetOptionalString(element, "parameters"),
            GetOptionalString(element, "body"),
            GetOptionalString(element, "folder"),
            GetOptionalString(element, "documentation"));
    }

    private static KustoScalarType ParseScalarType(string typeName)
    {
        KustoScalarType scalarType = typeName switch
        {
            nameof(KustoScalarType.Bool) => KustoScalarType.Bool,
            nameof(KustoScalarType.DateTime) => KustoScalarType.DateTime,
            nameof(KustoScalarType.FixedPoint) => KustoScalarType.FixedPoint,
            nameof(KustoScalarType.Dynamic) => KustoScalarType.Dynamic,
            nameof(KustoScalarType.Identifier) => KustoScalarType.Identifier,
            nameof(KustoScalarType.WholeNumber) => KustoScalarType.WholeNumber,
            nameof(KustoScalarType.WideInteger) => KustoScalarType.WideInteger,
            nameof(KustoScalarType.Real) => KustoScalarType.Real,
            nameof(KustoScalarType.Text) => KustoScalarType.Text,
            nameof(KustoScalarType.TimeSpan) => KustoScalarType.TimeSpan,
            _ => throw new InvalidDataException($"Unsupported cached scalar type {typeName}."),
        };

        return scalarType;
    }

    private static void WriteCluster(Utf8JsonWriter writer, KustoClusterConnection cluster)
    {
        writer.WriteStartObject();
        writer.WriteString("uri", cluster.ClusterUri.AbsoluteUri);
        writer.WriteString("displayName", cluster.DisplayName);

        if (cluster.FolderName is not null)
        {
            writer.WriteString("folderName", cluster.FolderName);
        }

        writer.WritePropertyName("databases");
        writer.WriteStartArray();

        foreach (KustoDatabaseConnection database in cluster.Databases)
        {
            WriteDatabase(writer, database);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteDatabase(Utf8JsonWriter writer, KustoDatabaseConnection database)
    {
        writer.WriteStartObject();
        writer.WriteString("name", database.Name);
        writer.WriteString("displayName", database.DisplayName);

        if (database.Schema is not null)
        {
            writer.WritePropertyName("tables");
            writer.WriteStartArray();

            foreach (KustoTableSchema table in database.Schema.Tables)
            {
                WriteTable(writer, table);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("functions");
            writer.WriteStartArray();

            foreach (KustoFunctionSchema function in database.Schema.Functions)
            {
                WriteFunction(writer, function);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteTable(Utf8JsonWriter writer, KustoTableSchema table)
    {
        writer.WriteStartObject();
        writer.WriteString("name", table.Name);
        writer.WritePropertyName("columns");
        writer.WriteStartArray();

        foreach (KustoColumnSchema column in table.Columns)
        {
            writer.WriteStartObject();
            writer.WriteString("name", column.Name);
            writer.WriteString("type", column.Type.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteFunction(Utf8JsonWriter writer, KustoFunctionSchema function)
    {
        writer.WriteStartObject();
        writer.WriteString("name", function.Name);
        writer.WriteString("parameters", function.Parameters);
        writer.WriteString("body", function.Body);

        if (function.Folder is not null)
        {
            writer.WriteString("folder", function.Folder);
        }

        if (function.Documentation is not null)
        {
            writer.WriteString("documentation", function.Documentation);
        }

        writer.WriteEndObject();
    }
}
