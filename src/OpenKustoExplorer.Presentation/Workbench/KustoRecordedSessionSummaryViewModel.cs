using System.Globalization;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one lightweight recorded-session catalog entry.
/// </summary>
public sealed class KustoRecordedSessionSummaryViewModel
{
    private readonly KustoRecordedSessionSummary summary;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedSessionSummaryViewModel"/> class.
    /// </summary>
    /// <param name="summary">The recorded-session summary.</param>
    public KustoRecordedSessionSummaryViewModel(KustoRecordedSessionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        this.summary = summary;
    }

    /// <summary>Gets the session identifier.</summary>
    public Guid Id => summary.Id;

    /// <summary>Gets the session name.</summary>
    public string Name => summary.Name;

    /// <summary>Gets the local last-updated time.</summary>
    public string LastUpdatedText => summary.LastUpdatedAtUtc
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Gets the execution count text.</summary>
    public string ExecutionCountText => summary.ExecutionCount == 1
        ? "1 query"
        : $"{summary.ExecutionCount:N0} queries";

    /// <summary>Gets the formatted logical size of the persisted session payload.</summary>
    public string StoredSizeText => $"{FormatByteSize(summary.StoredBytes)} stored";

    /// <summary>Gets an accessible summary.</summary>
    public string AutomationName => $"{Name}, {StoredSizeText}, {ExecutionCountText}, updated {LastUpdatedText}";

    private static string FormatByteSize(long bytes)
    {
        const double Kilobyte = 1024;
        const double Megabyte = Kilobyte * 1024;
        const double Gigabyte = Megabyte * 1024;
        return bytes switch
        {
            < 1024 => $"{bytes.ToString("N0", CultureInfo.InvariantCulture)} B",
            < 1024 * 1024 => $"{(bytes / Kilobyte).ToString("N1", CultureInfo.InvariantCulture)} KB",
            < 1024 * 1024 * 1024 => $"{(bytes / Megabyte).ToString("N1", CultureInfo.InvariantCulture)} MB",
            _ => $"{(bytes / Gigabyte).ToString("N1", CultureInfo.InvariantCulture)} GB",
        };
    }
}
