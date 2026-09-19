using OpenKustoExplorer.Kusto.Execution;

namespace OpenKustoExplorer.Infrastructure.Tests.Execution;

/// <summary>
/// Verifies Kusto query execution routing behavior.
/// </summary>
public sealed class KustoQueryServiceTests
{
    /// <summary>
    /// Verifies that only queries whose first non-whitespace character is a dot use management routing.
    /// </summary>
    /// <param name="queryText">The query text to classify.</param>
    /// <param name="expected">Whether the query is a management command.</param>
    [Theory]
    [InlineData(".show tables", true)]
    [InlineData(" \r\n.show database schema", true)]
    [InlineData("StormEvents | take 10", false)]
    [InlineData("print '.'", false)]
    public void IsManagementCommandClassifiesLeadingDot(string queryText, bool expected)
    {
        bool result = KustoExecutionService.IsManagementCommand(queryText);

        Assert.Equal(expected, result);
    }
}
