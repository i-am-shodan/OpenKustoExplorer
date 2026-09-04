using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Infrastructure.Connections;

namespace OpenKustoExplorer.Infrastructure.Tests.Connections;

/// <summary>
/// Verifies parsing of Kusto database schema management payloads.
/// </summary>
public sealed class KustoDatabaseSchemaParserTests
{
    /// <summary>
    /// Verifies that documented database JSON becomes ordered tables, columns, and Kusto scalar types.
    /// </summary>
    [Fact]
    public void ParseBuildsOrderedDatabaseSchema()
    {
        const string SchemaJson = """
            {
              "Databases": {
                "Telemetry": {
                  "Name": "Telemetry",
                  "Tables": {
                    "Events": {
                      "Name": "Events",
                      "OrderedColumns": [
                        { "Name": "Timestamp", "Type": "System.DateTime" },
                        { "Name": "DeviceId", "Type": "System.String" },
                        { "Name": "Value", "Type": "System.Double" },
                        { "Name": "Properties", "Type": "System.Object" }
                      ]
                    },
                    "Metrics": {
                      "Name": "Metrics",
                      "OrderedColumns": [
                        { "Name": "Count", "CslType": "long", "Type": "System.Int64" }
                      ]
                    }
                  }
                }
              }
            }
            """;

        KustoDatabaseSchema schema = KustoDatabaseSchemaParser.Parse(
            "adx.contoso.com",
            "Telemetry",
            SchemaJson);

        Assert.Equal("adx.contoso.com", schema.ClusterName);
        Assert.Equal("Telemetry", schema.DatabaseName);
        Assert.Equal(["Events", "Metrics"], schema.Tables.Select(table => table.Name));
        Assert.Equal(
            [KustoScalarType.DateTime, KustoScalarType.Text, KustoScalarType.Real, KustoScalarType.Dynamic],
            schema.Tables[0].Columns.Select(column => column.Type));
        Assert.Equal(KustoScalarType.WideInteger, Assert.Single(schema.Tables[1].Columns).Type);
    }

    /// <summary>
    /// Verifies that a future unknown scalar type remains available as dynamic data.
    /// </summary>
    [Fact]
    public void ParseMapsUnknownScalarTypeToDynamic()
    {
        const string SchemaJson = """
            {
              "Databases": {
                "Telemetry": {
                  "Tables": {
                    "Events": {
                      "OrderedColumns": [{ "Name": "FutureValue", "Type": "System.FutureScalar" }]
                    }
                  }
                }
              }
            }
            """;

        KustoDatabaseSchema schema = KustoDatabaseSchemaParser.Parse(
            "adx.contoso.com",
            "Telemetry",
            SchemaJson);

        Assert.Equal(KustoScalarType.Dynamic, Assert.Single(Assert.Single(schema.Tables).Columns).Type);
    }
}
