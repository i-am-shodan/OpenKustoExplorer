using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one visualization available to dashboard widgets.
/// </summary>
public sealed class KustoDashboardVisualizationOptionViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardVisualizationOptionViewModel"/> class.
    /// </summary>
    /// <param name="label">The concise display label.</param>
    /// <param name="kind">The visualization kind.</param>
    public KustoDashboardVisualizationOptionViewModel(string label, KustoVisualizationKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (!Enum.IsDefined(kind) || kind is KustoVisualizationKind.Table or KustoVisualizationKind.Graph)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Label = label;
        Kind = kind;
    }

    /// <summary>
    /// Gets the concise display label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the visualization kind.
    /// </summary>
    public KustoVisualizationKind Kind { get; }
}
