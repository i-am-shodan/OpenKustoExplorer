using OpenKustoExplorer.Desktop.Charts;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies shared chart geometry used by native and browser hosts.
/// </summary>
public sealed class KustoChartTests
{
    /// <summary>
    /// Verifies categorical values remain centered within the plot at both axis edges.
    /// </summary>
    /// <param name="normalizedPosition">The normalized category position.</param>
    /// <param name="expected">The expected plot coordinate.</param>
    [Theory]
    [InlineData(0, 20)]
    [InlineData(0.5, 50)]
    [InlineData(1, 80)]
    public void CategoricalPositionsReserveHalfSlotAtEdges(
        double normalizedPosition,
        double expected)
    {
        double actual = KustoChart.MapCategoricalPosition(
            start: 10,
            length: 80,
            normalizedPosition,
            slotLength: 20);

        Assert.Equal(expected, actual, precision: 10);
    }
}
