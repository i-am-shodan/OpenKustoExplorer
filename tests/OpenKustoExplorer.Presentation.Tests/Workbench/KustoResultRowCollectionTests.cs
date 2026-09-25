using System.Collections.Specialized;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Workbench;

/// <summary>
/// Verifies efficient visible result-row replacement behavior.
/// </summary>
public sealed class KustoResultRowCollectionTests
{
    /// <summary>
    /// Verifies an identical row-reference sequence does not reset item containers.
    /// </summary>
    [Fact]
    public void IdenticalProjectionDoesNotRaiseCollectionReset()
    {
        KustoResultColumn[] columns = [new KustoResultColumn("Value", "string")];
        KustoResultRowViewModel first = new(
            new KustoResultRow(["first"]),
            0,
            columns);
        KustoResultRowViewModel second = new(
            new KustoResultRow(["second"]),
            1,
            columns);
        KustoResultRowCollection rows = new();
        Assert.True(rows.ReplaceWith([first, second]));
        int resetCount = 0;
        rows.CollectionChanged += (_, eventArguments) =>
        {
            if (eventArguments.Action == NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }
        };

        bool changed = rows.ReplaceWith([first, second]);

        Assert.False(changed);
        Assert.Equal(0, resetCount);
        Assert.True(rows.ReplaceWith([second, first]));
        Assert.Equal(1, resetCount);
    }
}
