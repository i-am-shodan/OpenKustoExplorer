using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one manually selectable Kusto visualization type.
/// </summary>
public sealed class KustoVisualizationChoiceViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoVisualizationChoiceViewModel"/> class.
    /// </summary>
    /// <param name="kind">The visualization kind.</param>
    /// <param name="label">The concise display label.</param>
    /// <param name="selectAction">Renders the selected visualization.</param>
    private KustoVisualizationChoiceViewModel(
        KustoVisualizationKind kind,
        string label,
        Action<KustoVisualizationKind> selectAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(selectAction);

        Kind = kind;
        Label = label;
        SelectCommand = new RelayCommand(() => selectAction(Kind));
    }

    /// <summary>
    /// Gets the visualization kind.
    /// </summary>
    public KustoVisualizationKind Kind { get; }

    /// <summary>
    /// Gets the concise display label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the command that renders this visualization.
    /// </summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>
    /// Creates the complete set of manually selectable Kusto visualizations.
    /// </summary>
    /// <param name="selectAction">Renders the selected visualization.</param>
    /// <param name="includeGraph">Whether the live investigation graph destination is available.</param>
    /// <returns>The shared visualization choices in display order.</returns>
    internal static ReadOnlyCollection<KustoVisualizationChoiceViewModel> CreateAll(
        Action<KustoVisualizationKind> selectAction,
        bool includeGraph = false)
    {
        ArgumentNullException.ThrowIfNull(selectAction);

        KustoVisualizationChoiceViewModel[] choices =
        [
            new(KustoVisualizationKind.TimeChart, "Time", selectAction),
            new(KustoVisualizationKind.LineChart, "Line", selectAction),
            new(KustoVisualizationKind.ColumnChart, "Column", selectAction),
            new(KustoVisualizationKind.BarChart, "Bar", selectAction),
            new(KustoVisualizationKind.AreaChart, "Area", selectAction),
            new(KustoVisualizationKind.StackedAreaChart, "Stacked area", selectAction),
            new(KustoVisualizationKind.ScatterChart, "Scatter", selectAction),
            new(KustoVisualizationKind.PieChart, "Pie", selectAction),
            new(KustoVisualizationKind.TreeMap, "Treemap", selectAction),
            new(KustoVisualizationKind.Card, "Card", selectAction),
            new(KustoVisualizationKind.AnomalyChart, "Anomaly", selectAction),
            new(KustoVisualizationKind.LadderChart, "Ladder", selectAction),
            new(KustoVisualizationKind.PivotChart, "Pivot", selectAction),
            new(KustoVisualizationKind.TimePivot, "Time pivot", selectAction),
        ];

        if (includeGraph)
        {
            choices = [.. choices, new(KustoVisualizationKind.Graph, "Graph", selectAction)];
        }

        return Array.AsReadOnly(choices);
    }
}
