using System.Collections.ObjectModel;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Contains the rendering instructions attached to a Kusto query result.
/// </summary>
public sealed class KustoVisualization
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoVisualization"/> class.
    /// </summary>
    /// <param name="kind">The requested visualization.</param>
    /// <param name="title">The optional visualization title.</param>
    /// <param name="xColumn">The optional x-axis column.</param>
    /// <param name="xTitle">The optional x-axis title.</param>
    /// <param name="yTitle">The optional y-axis title.</param>
    /// <param name="seriesColumns">Columns whose combined values identify a series.</param>
    /// <param name="yColumns">Columns containing measured values.</param>
    /// <param name="anomalyColumns">Columns containing anomaly indicators.</param>
    /// <param name="kindOption">The optional visualization kind modifier.</param>
    /// <param name="legendVisible">Whether the legend should be displayed.</param>
    /// <param name="accumulate">Whether each measure accumulates its predecessors.</param>
    /// <param name="yMinimum">The optional fixed y-axis minimum.</param>
    /// <param name="yMaximum">The optional fixed y-axis maximum.</param>
    /// <param name="xAxisLogarithmic">Whether the x-axis uses logarithmic scaling.</param>
    /// <param name="yAxisLogarithmic">Whether the y-axis uses logarithmic scaling.</param>
    /// <param name="ySplit">The optional y-axis split mode.</param>
    public KustoVisualization(
        KustoVisualizationKind kind,
        string? title = null,
        string? xColumn = null,
        string? xTitle = null,
        string? yTitle = null,
        IEnumerable<string>? seriesColumns = null,
        IEnumerable<string>? yColumns = null,
        IEnumerable<string>? anomalyColumns = null,
        string? kindOption = null,
        bool legendVisible = true,
        bool accumulate = false,
        double? yMinimum = null,
        double? yMaximum = null,
        bool xAxisLogarithmic = false,
        bool yAxisLogarithmic = false,
        string? ySplit = null)
    {
        Kind = kind;
        Title = Normalize(title);
        XColumn = Normalize(xColumn);
        XTitle = Normalize(xTitle);
        YTitle = Normalize(yTitle);
        SeriesColumns = NormalizeColumns(seriesColumns);
        YColumns = NormalizeColumns(yColumns);
        AnomalyColumns = NormalizeColumns(anomalyColumns);
        KindOption = Normalize(kindOption);
        LegendVisible = legendVisible;
        Accumulate = accumulate;
        YMinimum = yMinimum;
        YMaximum = yMaximum;
        XAxisLogarithmic = xAxisLogarithmic;
        YAxisLogarithmic = yAxisLogarithmic;
        YSplit = Normalize(ySplit);
    }

    /// <summary>
    /// Gets the requested visualization.
    /// </summary>
    public KustoVisualizationKind Kind { get; }

    /// <summary>
    /// Gets the optional visualization title.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Gets the optional x-axis column.
    /// </summary>
    public string? XColumn { get; }

    /// <summary>
    /// Gets the optional x-axis title.
    /// </summary>
    public string? XTitle { get; }

    /// <summary>
    /// Gets the optional y-axis title.
    /// </summary>
    public string? YTitle { get; }

    /// <summary>
    /// Gets columns whose combined values identify a series.
    /// </summary>
    public IReadOnlyList<string> SeriesColumns { get; }

    /// <summary>
    /// Gets columns containing measured values.
    /// </summary>
    public IReadOnlyList<string> YColumns { get; }

    /// <summary>
    /// Gets columns containing anomaly indicators.
    /// </summary>
    public IReadOnlyList<string> AnomalyColumns { get; }

    /// <summary>
    /// Gets the optional visualization kind modifier.
    /// </summary>
    public string? KindOption { get; }

    /// <summary>
    /// Gets a value indicating whether the legend should be displayed.
    /// </summary>
    public bool LegendVisible { get; }

    /// <summary>
    /// Gets a value indicating whether each measure accumulates its predecessors.
    /// </summary>
    public bool Accumulate { get; }

    /// <summary>
    /// Gets the optional fixed y-axis minimum.
    /// </summary>
    public double? YMinimum { get; }

    /// <summary>
    /// Gets the optional fixed y-axis maximum.
    /// </summary>
    public double? YMaximum { get; }

    /// <summary>
    /// Gets a value indicating whether the x-axis uses logarithmic scaling.
    /// </summary>
    public bool XAxisLogarithmic { get; }

    /// <summary>
    /// Gets a value indicating whether the y-axis uses logarithmic scaling.
    /// </summary>
    public bool YAxisLogarithmic { get; }

    /// <summary>
    /// Gets the optional y-axis split mode.
    /// </summary>
    public string? YSplit { get; }

    /// <summary>
    /// Attempts to map a Kusto render visualization name to its stable kind.
    /// </summary>
    /// <param name="value">The Kusto render visualization name.</param>
    /// <param name="kind">The mapped visualization kind.</param>
    /// <returns><see langword="true"/> when the name is supported.</returns>
    public static bool TryParseKind(string? value, out KustoVisualizationKind kind)
    {
        kind = value?.Trim().ToLowerInvariant() switch
        {
            "anomalychart" => KustoVisualizationKind.AnomalyChart,
            "areachart" => KustoVisualizationKind.AreaChart,
            "barchart" => KustoVisualizationKind.BarChart,
            "card" => KustoVisualizationKind.Card,
            "columnchart" => KustoVisualizationKind.ColumnChart,
            "graph" => KustoVisualizationKind.Graph,
            "ladderchart" => KustoVisualizationKind.LadderChart,
            "linechart" => KustoVisualizationKind.LineChart,
            "piechart" => KustoVisualizationKind.PieChart,
            "pivotchart" => KustoVisualizationKind.PivotChart,
            "scatterchart" => KustoVisualizationKind.ScatterChart,
            "stackedareachart" => KustoVisualizationKind.StackedAreaChart,
            "table" => KustoVisualizationKind.Table,
            "timechart" => KustoVisualizationKind.TimeChart,
            "timepivot" => KustoVisualizationKind.TimePivot,
            "treemap" => KustoVisualizationKind.TreeMap,
            _ => default,
        };

        return value is not null && string.Equals(GetName(kind), value.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetName(KustoVisualizationKind kind)
    {
        string name = kind switch
        {
            KustoVisualizationKind.AnomalyChart => "anomalychart",
            KustoVisualizationKind.AreaChart => "areachart",
            KustoVisualizationKind.BarChart => "barchart",
            KustoVisualizationKind.Card => "card",
            KustoVisualizationKind.ColumnChart => "columnchart",
            KustoVisualizationKind.Graph => "graph",
            KustoVisualizationKind.LadderChart => "ladderchart",
            KustoVisualizationKind.LineChart => "linechart",
            KustoVisualizationKind.PieChart => "piechart",
            KustoVisualizationKind.PivotChart => "pivotchart",
            KustoVisualizationKind.ScatterChart => "scatterchart",
            KustoVisualizationKind.StackedAreaChart => "stackedareachart",
            KustoVisualizationKind.Table => "table",
            KustoVisualizationKind.TimeChart => "timechart",
            KustoVisualizationKind.TimePivot => "timepivot",
            KustoVisualizationKind.TreeMap => "treemap",
            _ => string.Empty,
        };

        return name;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ReadOnlyCollection<string> NormalizeColumns(IEnumerable<string>? columns)
    {
        string[] normalizedColumns = columns?
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Select(column => column.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        return Array.AsReadOnly(normalizedColumns);
    }
}
