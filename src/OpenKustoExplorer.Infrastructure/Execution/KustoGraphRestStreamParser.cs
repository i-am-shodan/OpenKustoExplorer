using System.Text.Json;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Infrastructure.Execution;

/// <summary>
/// Streams Kusto graph node and edge tables without materializing the complete REST response.
/// </summary>
internal static class KustoGraphRestStreamParser
{
    private const int InitialBufferSize = 16 * 1024;
    private const int MaximumMaterializedResultRowCount = 10_000;
    private const int MaximumTokenBufferSize = 16 * 1024 * 1024;

    /// <summary>
    /// Parses a successful graph export response and emits complete rows to staged storage.
    /// </summary>
    /// <param name="stream">The Kusto REST response stream.</param>
    /// <param name="plan">The validated graph export plan.</param>
    /// <param name="sink">The staged graph row sink.</param>
    /// <param name="duration">The measured query duration.</param>
    /// <param name="maximumResultRowCount">The maximum edge rows materialized for the Results view.</param>
    /// <param name="cancellationToken">A token that cancels parsing.</param>
    /// <returns>Streamed row counts and duration.</returns>
    internal static async Task<KustoGraphExportSummary> ParseAsync(
        Stream stream,
        KustoGraphQueryPlan plan,
        IKustoGraphExportSink sink,
        TimeSpan duration,
        int maximumResultRowCount = MaximumMaterializedResultRowCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumResultRowCount);
        byte[] buffer = new byte[InitialBufferSize];
        int bufferedByteCount = 0;
        JsonReaderState readerState = default;
        bool isFinalBlock = false;
        using KustoGraphRestParserState parserState = new(
            plan,
            sink,
            maximumResultRowCount);

        while (!isFinalBlock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBufferHasWriteSpace(ref buffer, bufferedByteCount);
            int bytesRead = await stream.ReadAsync(
                buffer.AsMemory(bufferedByteCount),
                cancellationToken).ConfigureAwait(false);
            isFinalBlock = bytesRead == 0;
            int availableByteCount = bufferedByteCount + bytesRead;
            Utf8JsonReader reader = new(
                buffer.AsSpan(0, availableByteCount),
                isFinalBlock,
                readerState);

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                parserState.Consume(ref reader);
            }

            int consumedByteCount = checked((int)reader.BytesConsumed);
            readerState = reader.CurrentState;
            bufferedByteCount = availableByteCount - consumedByteCount;
            buffer.AsSpan(consumedByteCount, bufferedByteCount).CopyTo(buffer);

            if (isFinalBlock && bufferedByteCount != 0)
            {
                throw new JsonException("Kusto returned an incomplete graph JSON response.");
            }
        }

        parserState.ValidateComplete();
        return new KustoGraphExportSummary(
            parserState.NodeCount,
            parserState.EdgeCount,
            duration,
            parserState.ResultTable);
    }

    private static void EnsureBufferHasWriteSpace(ref byte[] buffer, int bufferedByteCount)
    {
        if (bufferedByteCount == buffer.Length)
        {
            if (buffer.Length >= MaximumTokenBufferSize)
            {
                throw new InvalidDataException(
                    $"A Kusto graph JSON token exceeded {MaximumTokenBufferSize} bytes.");
            }

            int newLength = Math.Min(buffer.Length * 2, MaximumTokenBufferSize);
            Array.Resize(ref buffer, newLength);
        }
    }
}
