using OpenKustoExplorer.Application.Diagnostics;

namespace OpenKustoExplorer.Application.Tests.Diagnostics;

/// <summary>
/// Verifies optional performance trace output.
/// </summary>
public sealed class KustoPerformanceTraceTests
{
    /// <summary>
    /// Verifies measured operations produce parseable buffered records only when configured.
    /// </summary>
    [Fact]
    public void MeasureWritesConfiguredPerformanceLog()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-Performance-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "performance.tsv");
        string? originalPath = Environment.GetEnvironmentVariable(
            KustoPerformanceTrace.LogPathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(KustoPerformanceTrace.LogPathEnvironmentVariable, filePath);
            using (KustoPerformanceTrace.Measure("test.operation", 7))
            {
                Thread.SpinWait(100);
            }

            KustoPerformanceTrace.Flush();

            string[] lines = File.ReadAllLines(filePath);
            Assert.Equal(2, lines.Length);
            Assert.Equal(
                "timestamp_utc\toperation\tduration_ms\titem_count\tmanaged_bytes\tworking_set_bytes",
                lines[0]);
            string[] values = lines[1].Split('\t');
            Assert.Equal(6, values.Length);
            Assert.Equal("test.operation", values[1]);
            Assert.Equal("7", values[3]);
            Assert.True(double.TryParse(
                values[2],
                System.Globalization.CultureInfo.InvariantCulture,
                out double duration));
            Assert.True(duration >= 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                KustoPerformanceTrace.LogPathEnvironmentVariable,
                originalPath);
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }
}
