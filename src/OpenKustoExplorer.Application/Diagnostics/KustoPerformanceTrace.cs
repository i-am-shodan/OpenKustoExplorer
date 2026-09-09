using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;

namespace OpenKustoExplorer.Application.Diagnostics;

/// <summary>
/// Emits low-overhead operation timings through .NET metrics and an optional buffered trace file.
/// </summary>
public static class KustoPerformanceTrace
{
    /// <summary>Gets the environment variable that enables the buffered performance log.</summary>
    public const string LogPathEnvironmentVariable = "OPENKUSTOEXPLORER_PERF_LOG";

    private static readonly Meter DiagnosticsMeter = new("OpenKustoExplorer.Performance", "1.0.0");
    private static readonly Histogram<double> DurationHistogram = DiagnosticsMeter.CreateHistogram<double>(
        "operation.duration",
        "ms",
        "Elapsed time for instrumented Open Kusto Explorer operations.");

    private static readonly Lock SyncRoot = new();
    private static readonly long StartupTimestamp = Stopwatch.GetTimestamp();
    private static StringBuilder? bufferedLog;

    /// <summary>
    /// Starts timing one named operation.
    /// </summary>
    /// <param name="operationName">The stable operation name.</param>
    /// <param name="itemCount">The optional number of items processed.</param>
    /// <returns>A scope that records the elapsed time when disposed.</returns>
    public static OperationScope Measure(string operationName, int itemCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        return new OperationScope(operationName, itemCount, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Records elapsed process startup time for one milestone.
    /// </summary>
    /// <param name="milestoneName">The stable startup milestone name.</param>
    public static void RecordStartupMilestone(string milestoneName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(milestoneName);
        Record(milestoneName, Stopwatch.GetElapsedTime(StartupTimestamp), 0);
    }

    /// <summary>
    /// Flushes buffered profiling records to the path configured by
    /// <see cref="LogPathEnvironmentVariable"/>.
    /// </summary>
    public static void Flush()
    {
        string? logPath = Environment.GetEnvironmentVariable(LogPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        string? content;
        lock (SyncRoot)
        {
            content = bufferedLog?.ToString();
            bufferedLog?.Clear();
        }

        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        try
        {
            string fullPath = Path.GetFullPath(logPath);
            string? directoryPath = Path.GetDirectoryName(fullPath);
            if (directoryPath is not null)
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.AppendAllText(fullPath, content, new UTF8Encoding(false));
        }
        catch (IOException)
        {
            // Profiling output must never affect application behavior.
        }
        catch (UnauthorizedAccessException)
        {
            // Profiling output must never affect application behavior.
        }
    }

    private static void Record(string operationName, TimeSpan elapsed, int itemCount)
    {
        DurationHistogram.Record(
            elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("operation", operationName),
            new KeyValuePair<string, object?>("item.count", itemCount));
        string? logPath = Environment.GetEnvironmentVariable(LogPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        lock (SyncRoot)
        {
            bufferedLog ??= new StringBuilder(
                "timestamp_utc\toperation\tduration_ms\titem_count\tmanaged_bytes\tworking_set_bytes"
                + Environment.NewLine);
            bufferedLog.Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(operationName)
                .Append('\t')
                .Append(elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(itemCount.ToString(CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(GC.GetTotalMemory(false).ToString(CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(Environment.WorkingSet.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }
    }

    /// <summary>
    /// Records one operation duration when disposed.
    /// </summary>
    public readonly struct OperationScope : IDisposable
    {
        private readonly int itemCount;
        private readonly string operationName;
        private readonly long startTimestamp;

        /// <summary>
        /// Initializes a new instance of the <see cref="OperationScope"/> struct.
        /// </summary>
        /// <param name="operationName">The stable operation name.</param>
        /// <param name="itemCount">The number of items processed.</param>
        /// <param name="startTimestamp">The high-resolution start timestamp.</param>
        internal OperationScope(string operationName, int itemCount, long startTimestamp)
        {
            this.operationName = operationName;
            this.itemCount = itemCount;
            this.startTimestamp = startTimestamp;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Record(operationName, Stopwatch.GetElapsedTime(startTimestamp), itemCount);
        }
    }
}
