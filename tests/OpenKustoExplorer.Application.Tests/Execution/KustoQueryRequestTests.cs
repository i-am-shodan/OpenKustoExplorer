using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Application.Tests.Execution;

/// <summary>
/// Verifies Kusto execution request and result invariants.
/// </summary>
public sealed class KustoQueryRequestTests
{
    /// <summary>
    /// Verifies that valid requests preserve their immutable target and source text.
    /// </summary>
    [Fact]
    public void ConstructorPreservesValidRequest()
    {
        Uri clusterUri = new("https://help.kusto.windows.net");

        KustoQueryRequest request = new(clusterUri, "Samples", "StormEvents | take 10");

        Assert.Same(clusterUri, request.ClusterUri);
        Assert.Equal("Samples", request.DatabaseName);
        Assert.Equal("StormEvents | take 10", request.QueryText);
    }

    /// <summary>
    /// Verifies that query requests reject non-HTTPS cluster addresses.
    /// </summary>
    [Fact]
    public void ConstructorRejectsInsecureClusterUri()
    {
        Uri clusterUri = new("http://help.kusto.windows.net");

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new KustoQueryRequest(clusterUri, "Samples", "StormEvents"));

        Assert.Equal("clusterUri", exception.ParamName);
    }

    /// <summary>
    /// Verifies that materialized result collections do not retain mutable input arrays.
    /// </summary>
    [Fact]
    public void ResultCopiesMutableInputCollections()
    {
        KustoResultColumn[] columns = [new KustoResultColumn("State", "string")];
        KustoResultRow[] rows = [new KustoResultRow(["Texas"])];
        KustoResultTable[] tables = [new KustoResultTable("Result 1", columns, rows)];

        KustoQueryResult result = new(tables, TimeSpan.FromMilliseconds(10));
        tables[0] = new KustoResultTable("Replacement", [], []);
        columns[0] = new KustoResultColumn("Replacement", "string");
        rows[0] = new KustoResultRow(["Replacement"]);

        Assert.Equal("Result 1", result.Tables[0].Name);
        Assert.Equal("State", result.Tables[0].Columns[0].Name);
        Assert.Equal("Texas", result.Tables[0].Rows[0].Values[0]);
    }
}
