namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one human-readable chart-axis tick.
/// </summary>
public sealed class KustoChartAxisTickViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoChartAxisTickViewModel"/> class.
    /// </summary>
    /// <param name="position">The normalized axis position from zero to one.</param>
    /// <param name="label">The formatted tick label.</param>
    public KustoChartAxisTickViewModel(double position, string label)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Position = position;
        Label = label;
    }

    /// <summary>
    /// Gets the normalized axis position from zero to one.
    /// </summary>
    public double Position { get; }

    /// <summary>
    /// Gets the formatted tick label.
    /// </summary>
    public string Label { get; }
}
