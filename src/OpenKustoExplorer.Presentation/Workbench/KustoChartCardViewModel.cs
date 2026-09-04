namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one scalar value in a Kusto card visualization.
/// </summary>
public sealed class KustoChartCardViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoChartCardViewModel"/> class.
    /// </summary>
    /// <param name="label">The source column label.</param>
    /// <param name="value">The scalar display value.</param>
    public KustoChartCardViewModel(string label, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(value);

        Label = label;
        Value = value;
    }

    /// <summary>
    /// Gets the source column label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the scalar display value.
    /// </summary>
    public string Value { get; }
}
