using Avalonia;
using Avalonia.Controls;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Desktop.Controls;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies proportional result-row layout behavior.
/// </summary>
public sealed class KustoResultRowPanelTests
{
    /// <summary>
    /// Verifies spare row width is distributed in proportion to content-fit column widths.
    /// </summary>
    [Fact]
    public void ArrangeDistributesSpareWidthProportionally()
    {
        KustoResultRowPanel panel = CreatePanel();

        panel.Measure(new Size(double.PositiveInfinity, 30));
        panel.Arrange(new Rect(0, 0, 600, 30));

        Assert.Equal(200, panel.Children[0].Bounds.Width, 3);
        Assert.Equal(400, panel.Children[1].Bounds.Width, 3);
    }

    /// <summary>
    /// Verifies content-fit column widths are retained when no spare row width is available.
    /// </summary>
    [Fact]
    public void ArrangeRetainsContentFitWidthsWithoutSpareSpace()
    {
        KustoResultRowPanel panel = CreatePanel();

        panel.Measure(new Size(double.PositiveInfinity, 30));
        panel.Arrange(new Rect(0, 0, 300, 30));

        Assert.Equal(100, panel.Children[0].Bounds.Width, 3);
        Assert.Equal(200, panel.Children[1].Bounds.Width, 3);
    }

    private static KustoResultRowPanel CreatePanel()
    {
        KustoResultColumn[] columns =
        [
            new KustoResultColumn("Name", "string"),
            new KustoResultColumn("Value", "long"),
        ];
        KustoResultRowViewModel row = new(
            new KustoResultRow(["Alice", "42"]),
            0,
            columns,
            [100, 200]);
        KustoResultRowPanel panel = new();

        foreach (KustoResultCellViewModel cell in row.Cells)
        {
            panel.Children.Add(new Border { DataContext = cell });
        }

        return panel;
    }
}
