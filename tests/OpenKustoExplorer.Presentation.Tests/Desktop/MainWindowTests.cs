using OpenKustoExplorer.Desktop;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies desktop workbench interactions.
/// </summary>
public sealed class MainWindowTests
{
    /// <summary>
    /// Verifies wheel input moves result rows in the expected direction without crossing a boundary.
    /// </summary>
    /// <param name="currentOffset">The starting vertical offset.</param>
    /// <param name="maximumOffset">The maximum vertical offset.</param>
    /// <param name="wheelDelta">The requested vertical wheel delta.</param>
    /// <param name="expectedOffset">The expected bounded vertical offset.</param>
    [Theory]
    [InlineData(300, 1000, 1, 210)]
    [InlineData(300, 1000, -1, 390)]
    [InlineData(30, 1000, 1, 0)]
    [InlineData(970, 1000, -1, 1000)]
    public void ResultWheelOffsetMovesAndClamps(
        double currentOffset,
        double maximumOffset,
        double wheelDelta,
        double expectedOffset)
    {
        double offset = MainWindow.CalculateResultWheelOffset(
            currentOffset,
            maximumOffset,
            wheelDelta);

        Assert.Equal(expectedOffset, offset);
    }
}
