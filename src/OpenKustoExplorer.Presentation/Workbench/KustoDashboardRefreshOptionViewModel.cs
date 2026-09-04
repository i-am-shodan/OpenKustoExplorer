namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one dashboard widget refresh interval.
/// </summary>
public sealed class KustoDashboardRefreshOptionViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardRefreshOptionViewModel"/> class.
    /// </summary>
    /// <param name="label">The concise display label.</param>
    /// <param name="interval">The positive refresh interval.</param>
    public KustoDashboardRefreshOptionViewModel(string label, TimeSpan interval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        Label = label;
        Interval = interval;
    }

    /// <summary>
    /// Gets the concise display label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the refresh interval.
    /// </summary>
    public TimeSpan Interval { get; }
}
