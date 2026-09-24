using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Infrastructure.Connections;

namespace OpenKustoExplorer.Infrastructure.Tests.Connections;

/// <summary>
/// Verifies durable connection catalog persistence.
/// </summary>
public sealed class FileKustoConnectionStoreTests
{
    /// <summary>
    /// Verifies that clusters, databases, tables, columns, and scalar types round-trip in order.
    /// </summary>
    [Fact]
    public void SaveAndLoadRoundTripsCatalogHierarchy()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "connections.json");

        try
        {
            KustoDatabaseSchema schema = new(
                "adx.contoso.com",
                "Telemetry",
                [
                    new KustoTableSchema(
                        "Events",
                        [
                            new KustoColumnSchema("Timestamp", KustoScalarType.DateTime),
                            new KustoColumnSchema("Payload", KustoScalarType.Dynamic),
                        ]),
                ],
                [
                    new KustoFunctionSchema(
                        "RecentEvents",
                        "(lookback: timespan)",
                        "{ Events | where Timestamp > ago(lookback) }",
                        "Operations",
                        "Returns recent events"),
                ]);
            KustoConnectionCatalog catalog = new(
                [
                    new KustoClusterConnection(
                        new Uri("https://adx.contoso.com"),
                        "Contoso ADX",
                        [new KustoDatabaseConnection("Telemetry", "Telemetry", schema)],
                        "Production"),
                ]);
            FileKustoConnectionStore store = new(filePath);

            store.Save(catalog);
            KustoConnectionCatalog restored = store.Load();

            KustoClusterConnection cluster = Assert.Single(restored.Clusters);
            Assert.Equal("Contoso ADX", cluster.DisplayName);
            Assert.Equal("Production", cluster.FolderName);
            KustoDatabaseConnection database = Assert.Single(cluster.Databases);
            Assert.NotNull(database.Schema);
            KustoTableSchema table = Assert.Single(database.Schema.Tables);
            Assert.Equal("Events", table.Name);
            Assert.Equal(KustoScalarType.DateTime, table.Columns[0].Type);
            Assert.Equal(KustoScalarType.Dynamic, table.Columns[1].Type);
            KustoFunctionSchema function = Assert.Single(database.Schema.Functions);
            Assert.Equal("RecentEvents(lookback: timespan)", function.Signature);
            Assert.Equal("Operations", function.Folder);
            Assert.Equal("Returns recent events", function.Documentation);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies that corrupt cache JSON does not prevent startup.
    /// </summary>
    [Fact]
    public void LoadReturnsEmptyCatalogForCorruptJson()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "connections.json");

        try
        {
            File.WriteAllText(filePath, "not json");
            FileKustoConnectionStore store = new(filePath);

            KustoConnectionCatalog catalog = store.Load();

            Assert.Empty(catalog.Clusters);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies that a malformed persisted cluster URI is preserved without blocking startup.
    /// </summary>
    [Fact]
    public void LoadReturnsEmptyCatalogForMalformedClusterUri()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "connections.json");
        const string Catalog = """
            {
              "version": 1,
              "clusters": [{
                "uri": "not-an-absolute-uri",
                "displayName": "Invalid",
                "databases": []
              }]
            }
            """;

        try
        {
            File.WriteAllText(filePath, Catalog);
            FileKustoConnectionStore store = new(filePath);

            KustoConnectionCatalog catalog = store.Load();

            Assert.Empty(catalog.Clusters);
            Assert.False(File.Exists(filePath));
            Assert.Single(Directory.GetFiles(directoryPath, "connections.json.corrupt-*.bak"));
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies that pre-function schema caches retain their connection while requesting one lazy refresh.
    /// </summary>
    [Fact]
    public void LoadInvalidatesLegacyTableOnlySchemaCache()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "connections.json");
        const string LegacyCatalog = """
                        {
                            "version": 1,
                            "clusters": [{
                                "uri": "https://adx.contoso.com/",
                                "displayName": "Contoso ADX",
                                "databases": [{
                                    "name": "Telemetry",
                                    "displayName": "Telemetry",
                                    "tables": [{ "name": "Events", "columns": [] }]
                                }]
                            }]
                        }
                        """;

        try
        {
            File.WriteAllText(filePath, LegacyCatalog);
            FileKustoConnectionStore store = new(filePath);

            KustoDatabaseConnection database = Assert.Single(Assert.Single(store.Load().Clusters).Databases);

            Assert.Equal("Telemetry", database.Name);
            Assert.Null(database.Schema);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }
}
