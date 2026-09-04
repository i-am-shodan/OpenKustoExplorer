namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one chartable x/y value and its hover text.
/// </summary>
public sealed class KustoChartPointViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoChartPointViewModel"/> class.
    /// </summary>
    /// <param name="x">The numeric x-axis coordinate.</param>
    /// <param name="xText">The human-readable x-axis value.</param>
    /// <param name="value">The numeric measured value.</param>
    /// <param name="valueText">The human-readable measured value.</param>
    /// <param name="isAnomaly">Whether this point is marked as anomalous.</param>
    public KustoChartPointViewModel(
        double x,
        string xText,
        double value,
        string valueText,
        bool isAnomaly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xText);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueText);

        X = x;
        XText = xText;
        Value = value;
        ValueText = valueText;
        IsAnomaly = isAnomaly;
    }

    /// <summary>
    /// Gets the numeric x-axis coordinate.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the human-readable x-axis value.
    /// </summary>
    public string XText { get; }

    /// <summary>
    /// Gets the numeric measured value.
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Gets the human-readable measured value.
    /// </summary>
    public string ValueText { get; }

    /// <summary>
    /// Gets a value indicating whether this point is marked as anomalous.
    /// </summary>
    public bool IsAnomaly { get; }
}
