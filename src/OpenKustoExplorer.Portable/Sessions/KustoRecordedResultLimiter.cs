using System.Text;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Portable.Sessions;

/// <summary>
/// Applies deterministic row and byte budgets to retained query results.
/// </summary>
public static class KustoRecordedResultLimiter
{
    /// <summary>
    /// Retains complete rows in server order until either supplied budget is exhausted.
    /// </summary>
    /// <param name="result">The materialized query result.</param>
    /// <param name="maximumRows">The maximum total rows across all result tables.</param>
    /// <param name="maximumBytes">The maximum estimated retained row bytes.</param>
    /// <returns>The original result when it fits, or a bounded result marked as truncated.</returns>
    public static KustoQueryResult Limit(
        KustoQueryResult result,
        int maximumRows,
        long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        int remainingRows = maximumRows;
        long remainingBytes = maximumBytes;
        bool wasTruncated = false;
        bool capacityExhausted = false;
        List<KustoResultTable> tables = [];
        foreach (KustoResultTable table in result.Tables)
        {
            List<KustoResultRow> retainedRows = [];
            foreach (KustoResultRow row in table.Rows)
            {
                long estimatedBytes = EstimateRowBytes(table.Columns, row);
                if (capacityExhausted || remainingRows == 0 || estimatedBytes > remainingBytes)
                {
                    capacityExhausted = true;
                    wasTruncated = true;
                    break;
                }

                retainedRows.Add(row);
                remainingRows--;
                remainingBytes -= estimatedBytes;
            }

            wasTruncated |= retainedRows.Count != table.Rows.Count;
            tables.Add(new KustoResultTable(table.Name, table.Columns, retainedRows));
        }

        return wasTruncated
            ? new KustoQueryResult(
                tables,
                result.Duration,
                result.Visualization,
                KustoQueryResultCompleteness.RecordLimitReached)
            : result;
    }

    private static long EstimateRowBytes(
        IReadOnlyList<KustoResultColumn> columns,
        KustoResultRow row)
    {
        const int FixedRowBytes = 256;
        long estimatedBytes = FixedRowBytes;
        for (int index = 0; index < row.ResultValues.Count; index++)
        {
            KustoResultValue value = row.ResultValues[index];
            int displayBytes = Encoding.UTF8.GetByteCount(value.DisplayText);
            int rawBytes = value.RawJson is null
                ? displayBytes
                : Encoding.UTF8.GetByteCount(value.RawJson);
            estimatedBytes += (displayBytes * 3L) + (rawBytes * 2L) + 128;

            if (index < columns.Count)
            {
                estimatedBytes += Encoding.UTF8.GetByteCount(columns[index].Name);
                estimatedBytes += Encoding.UTF8.GetByteCount(columns[index].TypeName);
            }
        }

        return estimatedBytes;
    }
}
