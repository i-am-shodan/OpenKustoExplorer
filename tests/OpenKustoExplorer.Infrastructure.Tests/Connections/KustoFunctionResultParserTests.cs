using System.Collections.ObjectModel;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Infrastructure.Connections;

namespace OpenKustoExplorer.Infrastructure.Tests.Connections;

/// <summary>
/// Verifies stored-function management result parsing.
/// </summary>
public sealed class KustoFunctionResultParserTests
{
    /// <summary>
    /// Verifies that documented function columns become Explorer-ready immutable metadata.
    /// </summary>
    [Fact]
    public void ParseBuildsStoredFunctionSchemas()
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [
                new KustoResultColumn("Name", "string"),
                new KustoResultColumn("Parameters", "string"),
                new KustoResultColumn("Body", "string"),
                new KustoResultColumn("Folder", "string"),
                new KustoResultColumn("DocString", "string"),
            ],
            [
                new KustoResultRow(
                    ["RecentErrors", "(lookback: timespan)", "{ Errors | where Time > ago(lookback) }", "Operations", "Recent failures"]),
                new KustoResultRow(["AllEvents", "()", "{ Events }", string.Empty, string.Empty]),
            ]);

        ReadOnlyCollection<KustoFunctionSchema> functions = KustoFunctionResultParser.Parse([table]);

        Assert.Equal(2, functions.Count);
        Assert.Equal("RecentErrors(lookback: timespan)", functions[0].Signature);
        Assert.Equal("Operations", functions[0].Folder);
        Assert.Equal("Recent failures", functions[0].Documentation);
        Assert.Contains("Errors", functions[0].Body, StringComparison.Ordinal);
        Assert.Null(functions[1].Folder);
        Assert.Null(functions[1].Documentation);
    }
}
