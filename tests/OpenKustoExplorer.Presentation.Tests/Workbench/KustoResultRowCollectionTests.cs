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

    /// <summary>
    /// Verifies repeated recording annotation passes do not publish redundant cell notifications.
    /// </summary>
    [Fact]
    public void IdenticalRecordingAnnotationDoesNotRaisePropertyChanges()
    {
        KustoResultColumn[] columns = [new KustoResultColumn("Value", "string")];
        KustoResultRowViewModel row = new(new KustoResultRow(["value"]), 0, columns);
        KustoResultCellViewModel cell = Assert.Single(row.Cells);
        cell.SetRecordingAnnotation(
            matchesInterest: true,
            isPertinent: false,
            isStart: false,
            isEnd: false,
            matchesManualInterest: false,
            accentColorHex: "#001122",
            highlightColorHex: "#33112233");
        int propertyChangeCount = 0;
        cell.PropertyChanged += (_, _) => propertyChangeCount++;

        cell.SetRecordingAnnotation(
            matchesInterest: true,
            isPertinent: false,
            isStart: false,
            isEnd: false,
            matchesManualInterest: false,
            accentColorHex: "#001122",
            highlightColorHex: "#33112233");

        Assert.Equal(0, propertyChangeCount);
    }
}
