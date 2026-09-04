using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Infers and presents all visualization types supported by the Kusto.Explorer render operator.
/// </summary>
public sealed class KustoVisualizationViewModel : ObservableObject
{
    private const int MaximumSeriesCount = 48;
    private const int TargetTickCount = 5;

    private static readonly string[] SeriesColors =
    [
        "#3675C8",
        "#D64545",
        "#27864A",
        "#C56A16",
        "#7656B5",
        "#15858A",
        "#B94D7D",
        "#68737D",
        "#8A6A16",
        "#2D8C72",
        "#A74A32",
        "#4F62A8",
    ];

    private int revision;

    private KustoVisualizationViewModel(
        KustoVisualization visualization,
        string title,
        string xAxisTitle,
        string yAxisTitle,
        KustoChartAxisKind xAxisKind,
        double xMinimum,
        double xMaximum,
        double yMinimum,
        double yMaximum,
        IEnumerable<KustoChartAxisTickViewModel> xAxisTicks,
        IEnumerable<KustoChartAxisTickViewModel> yAxisTicks,
        IEnumerable<KustoChartSeriesViewModel> series,
        IEnumerable<KustoChartCardViewModel> cards)
    {
        Kind = visualization.Kind;
        KindOption = visualization.KindOption;
        Title = title;
        XAxisTitle = xAxisTitle;
        YAxisTitle = yAxisTitle;
        XAxisKind = xAxisKind;
        XMinimum = xMinimum;
        XMaximum = xMaximum;
        YMinimum = yMinimum;
        YMaximum = yMaximum;
        XAxisTicks = Array.AsReadOnly(xAxisTicks.ToArray());
        YAxisTicks = Array.AsReadOnly(yAxisTicks.Reverse().ToArray());
        IsHorizontalBar = Kind == KustoVisualizationKind.BarChart;
        HorizontalAxisTicks = IsHorizontalBar
            ? Array.AsReadOnly(YAxisTicks.Reverse().ToArray())
            : XAxisTicks;
        VerticalAxisTicks = IsHorizontalBar ? XAxisTicks : YAxisTicks;
        HorizontalAxisTitle = IsHorizontalBar ? YAxisTitle : XAxisTitle;
        VerticalAxisTitle = IsHorizontalBar ? XAxisTitle : YAxisTitle;
        Series = Array.AsReadOnly(series.ToArray());
        VisibleSeries = new ObservableCollection<KustoChartSeriesViewModel>(Series);
        foreach (KustoChartSeriesViewModel chartSeries in Series)
        {
            chartSeries.PropertyChanged += OnSeriesPropertyChanged;
        }

        ToggleAllSeriesVisibilityCommand = new RelayCommand(
            ToggleAllSeriesVisibility,
            () => Series.Count > 0);
        Cards = Array.AsReadOnly(cards.ToArray());
        LegendVisible = visualization.LegendVisible && Series.Count > 0;
        IsStacked = Kind == KustoVisualizationKind.StackedAreaChart
            || string.Equals(KindOption, "stacked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(KindOption, "stacked100", StringComparison.OrdinalIgnoreCase);
        IsStacked100 = string.Equals(KindOption, "stacked100", StringComparison.OrdinalIgnoreCase);
        XAxisLogarithmic = visualization.XAxisLogarithmic;
        YAxisLogarithmic = visualization.YAxisLogarithmic;
    }

    /// <summary>
    /// Gets the requested Kusto visualization type.
    /// </summary>
    public KustoVisualizationKind Kind { get; }

    /// <summary>
    /// Gets the optional Kusto visualization kind modifier.
    /// </summary>
    public string? KindOption { get; }

    /// <summary>
    /// Gets the visualization title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the x-axis title.
    /// </summary>
    public string XAxisTitle { get; }

    /// <summary>
    /// Gets the y-axis title.
    /// </summary>
    public string YAxisTitle { get; }

    /// <summary>
    /// Gets the x-axis value domain.
    /// </summary>
    public KustoChartAxisKind XAxisKind { get; }

    /// <summary>
    /// Gets the plotted x-axis minimum.
    /// </summary>
    public double XMinimum { get; }

    /// <summary>
    /// Gets the plotted x-axis maximum.
    /// </summary>
    public double XMaximum { get; }

    /// <summary>
    /// Gets the plotted y-axis minimum.
    /// </summary>
    public double YMinimum { get; }

    /// <summary>
    /// Gets the plotted y-axis maximum.
    /// </summary>
    public double YMaximum { get; }

    /// <summary>
    /// Gets human-readable x-axis ticks.
    /// </summary>
    public IReadOnlyList<KustoChartAxisTickViewModel> XAxisTicks { get; }

    /// <summary>
    /// Gets human-readable y-axis ticks ordered from maximum to minimum.
    /// </summary>
    public IReadOnlyList<KustoChartAxisTickViewModel> YAxisTicks { get; }

    /// <summary>
    /// Gets tick labels shown along the physical horizontal axis.
    /// </summary>
    public IReadOnlyList<KustoChartAxisTickViewModel> HorizontalAxisTicks { get; }

    /// <summary>
    /// Gets tick labels shown along the physical vertical axis.
    /// </summary>
    public IReadOnlyList<KustoChartAxisTickViewModel> VerticalAxisTicks { get; }

    /// <summary>
    /// Gets the title shown along the physical horizontal axis.
    /// </summary>
    public string HorizontalAxisTitle { get; }

    /// <summary>
    /// Gets the title shown along the physical vertical axis.
    /// </summary>
    public string VerticalAxisTitle { get; }

    /// <summary>
    /// Gets the inferred chart series.
    /// </summary>
    public IReadOnlyList<KustoChartSeriesViewModel> Series { get; }

    /// <summary>
    /// Gets the series currently enabled for rendering.
    /// </summary>
    public ObservableCollection<KustoChartSeriesViewModel> VisibleSeries { get; }

    /// <summary>
    /// Gets the command that hides all visible series or restores all hidden series.
    /// </summary>
    public IRelayCommand ToggleAllSeriesVisibilityCommand { get; }

    /// <summary>
    /// Gets a value indicating whether every chart series is currently visible.
    /// </summary>
    public bool AreAllSeriesVisible => Series.Count > 0 && VisibleSeries.Count == Series.Count;

    /// <summary>
    /// Gets the action performed by the chart-wide visibility toggle.
    /// </summary>
    public string AllSeriesVisibilityActionText => AreAllSeriesVisible
        ? "Hide all series"
        : "Show all series";

    /// <summary>
    /// Gets the revision incremented whenever interactive visualization state changes.
    /// </summary>
    public int Revision
    {
        get => revision;
        private set => SetProperty(ref revision, value);
    }

    /// <summary>
    /// Gets scalar values for a card visualization.
    /// </summary>
    public IReadOnlyList<KustoChartCardViewModel> Cards { get; }

    /// <summary>
    /// Gets a value indicating whether a legend should be shown.
    /// </summary>
    public bool LegendVisible { get; }

    /// <summary>
    /// Gets a value indicating whether values should be stacked.
    /// </summary>
    public bool IsStacked { get; }

    /// <summary>
    /// Gets a value indicating whether stacked values should be normalized to 100 percent.
    /// </summary>
    public bool IsStacked100 { get; }

    /// <summary>
    /// Gets a value indicating whether horizontal bars swap the logical axes.
    /// </summary>
    public bool IsHorizontalBar { get; }

    /// <summary>
    /// Gets a value indicating whether the logical x-axis uses logarithmic scaling.
    /// </summary>
    public bool XAxisLogarithmic { get; }

    /// <summary>
    /// Gets a value indicating whether the logical y-axis uses logarithmic scaling.
    /// </summary>
    public bool YAxisLogarithmic { get; }

    /// <summary>
    /// Gets a value indicating whether this visualization uses scalar cards.
    /// </summary>
    public bool IsCard => Kind == KustoVisualizationKind.Card;

    /// <summary>
    /// Gets a value indicating whether this visualization uses a plotted canvas.
    /// </summary>
    public bool IsPlot => Kind is not KustoVisualizationKind.Card and not KustoVisualizationKind.Table;

    /// <summary>
    /// Attempts to infer chart roles and series from a result table and Kusto render instructions.
    /// </summary>
    /// <param name="table">The primary result table.</param>
    /// <param name="visualization">The Kusto render instructions.</param>
    /// <param name="viewModel">The inferred visualization, or <see langword="null"/>.</param>
    /// <param name="message">A concise success or validation message.</param>
    /// <returns><see langword="true"/> when the result can be rendered.</returns>
    public static bool TryCreate(
        KustoResultTable table,
        KustoVisualization visualization,
        out KustoVisualizationViewModel? viewModel,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(visualization);

        viewModel = null;
        message = $"{GetDisplayName(visualization.Kind)} unavailable";

        if (visualization.Kind == KustoVisualizationKind.Card)
        {
            viewModel = CreateCard(table, visualization);
            message = viewModel is null ? "Results contain no row for a card." : "Rendered card";
        }
        else if (visualization.Kind != KustoVisualizationKind.Table)
        {
            viewModel = CreatePlot(table, visualization, out message);
        }

        return viewModel is not null;
    }

    /// <summary>
    /// Formats a numeric axis or hover value with compact human-readable units.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <returns>The compact invariant-culture value.</returns>
    public static string FormatNumber(double value)
    {
        double absoluteValue = Math.Abs(value);
        double divisor = 1;
        string suffix = string.Empty;

        if (absoluteValue >= 1_000_000_000_000)
        {
            divisor = 1_000_000_000_000;
            suffix = "T";
        }
        else if (absoluteValue >= 1_000_000_000)
        {
            divisor = 1_000_000_000;
            suffix = "B";
        }
        else if (absoluteValue >= 1_000_000)
        {
            divisor = 1_000_000;
            suffix = "M";
        }
        else if (absoluteValue >= 1_000)
        {
            divisor = 1_000;
            suffix = "K";
        }

        double scaledValue = value / divisor;
        string format = "0.##";
        if (Math.Abs(scaledValue) >= 100)
        {
            format = "0";
        }
        else if (Math.Abs(scaledValue) >= 10)
        {
            format = "0.#";
        }

        string text = scaledValue.ToString(format, CultureInfo.InvariantCulture) + suffix;
        if (absoluteValue > 0 && absoluteValue < 0.001)
        {
            text = value.ToString("0.##E+0", CultureInfo.InvariantCulture);
        }

        return text;
    }

    private static KustoVisualizationViewModel? CreateCard(
        KustoResultTable table,
        KustoVisualization visualization)
    {
        KustoResultRow? row = table.Rows.Count > 0 ? table.Rows[0] : null;
        KustoVisualizationViewModel? viewModel = null;

        if (row is not null)
        {
            List<KustoChartCardViewModel> cards = [];
            for (int index = 0; index < table.Columns.Count && index < row.Values.Count; index++)
            {
                cards.Add(new KustoChartCardViewModel(table.Columns[index].Name, row.Values[index]));
            }

            viewModel = new KustoVisualizationViewModel(
                visualization,
                visualization.Title ?? "Card",
                string.Empty,
                string.Empty,
                KustoChartAxisKind.Category,
                0,
                1,
                0,
                1,
                [],
                [],
                [],
                cards);
        }

        return viewModel;
    }

    private static KustoVisualizationViewModel? CreatePlot(
        KustoResultTable table,
        KustoVisualization visualization,
        out string message)
    {
        int xColumnIndex = ResolveXColumnIndex(table.Columns, visualization);
        int[] anomalyColumnIndexes = ResolveColumnIndexes(table.Columns, visualization.AnomalyColumns);
        int[] yColumnIndexes = ResolveYColumnIndexes(table.Columns, visualization, xColumnIndex, anomalyColumnIndexes);
        int[] seriesColumnIndexes = ResolveSeriesColumnIndexes(
            table.Columns,
            visualization,
            xColumnIndex,
            yColumnIndexes,
            anomalyColumnIndexes);
        KustoVisualizationViewModel? viewModel = null;
        message = GetMissingRoleMessage(visualization.Kind, xColumnIndex, yColumnIndexes.Length);

        if (xColumnIndex >= 0 && yColumnIndexes.Length > 0)
        {
            KustoChartAxisKind axisKind = GetAxisKind(table.Columns[xColumnIndex]);
            List<string> categories = [];
            ReadOnlyCollection<KustoChartSeriesViewModel> series = IsPartitionVisualization(visualization.Kind)
                ? CreatePartitionSeries(table, xColumnIndex, yColumnIndexes[0])
                : CreateContinuousSeries(
                    table,
                    visualization,
                    axisKind,
                    xColumnIndex,
                    yColumnIndexes,
                    seriesColumnIndexes,
                    anomalyColumnIndexes,
                    categories);

            if (series.Count > 0)
            {
                viewModel = CreatePlotViewModel(
                    table,
                    visualization,
                    axisKind,
                    xColumnIndex,
                    series,
                    categories);
                message = $"Rendered {GetDisplayName(visualization.Kind)} with {series.Count:N0} series";
            }
            else
            {
                message = "No rows contain compatible values for this visualization.";
            }
        }

        return viewModel;
    }

    private static ReadOnlyCollection<KustoChartSeriesViewModel> CreateContinuousSeries(
        KustoResultTable table,
        KustoVisualization visualization,
        KustoChartAxisKind axisKind,
        int xColumnIndex,
        int[] yColumnIndexes,
        int[] seriesColumnIndexes,
        int[] anomalyColumnIndexes,
        List<string> categories)
    {
        Dictionary<string, List<KustoChartPointViewModel>> pointsBySeries = new(StringComparer.OrdinalIgnoreCase);
        List<string> seriesOrder = [];
        Dictionary<string, double> accumulatedValues = new(StringComparer.OrdinalIgnoreCase);

        foreach (IReadOnlyList<string> values in table.Rows.Select(row => row.Values))
        {
            AddContinuousRow(
                table,
                visualization,
                axisKind,
                xColumnIndex,
                yColumnIndexes,
                seriesColumnIndexes,
                anomalyColumnIndexes,
                categories,
                values,
                pointsBySeries,
                seriesOrder,
                accumulatedValues);
        }

        KustoChartSeriesViewModel[] series = seriesOrder
            .Select((name, index) => new KustoChartSeriesViewModel(
                name,
                SeriesColors[index % SeriesColors.Length],
                pointsBySeries[name]))
            .ToArray();

        return Array.AsReadOnly(series);
    }

    private static void AddContinuousRow(
        KustoResultTable table,
        KustoVisualization visualization,
        KustoChartAxisKind axisKind,
        int xColumnIndex,
        int[] yColumnIndexes,
        int[] seriesColumnIndexes,
        int[] anomalyColumnIndexes,
        List<string> categories,
        IReadOnlyList<string> values,
        Dictionary<string, List<KustoChartPointViewModel>> pointsBySeries,
        List<string> seriesOrder,
        Dictionary<string, double> accumulatedValues)
    {
        bool hasX = TryGetXValue(values, xColumnIndex, axisKind, categories, out double x, out string xText);

        if (hasX)
        {
            string dimensionName = GetDimensionName(values, seriesColumnIndexes);
            bool isAnomaly = IsAnomaly(values, anomalyColumnIndexes);

            foreach (int yColumnIndex in yColumnIndexes)
            {
                AddContinuousPoint(
                    table,
                    visualization.Accumulate,
                    yColumnIndexes.Length,
                    yColumnIndex,
                    dimensionName,
                    isAnomaly,
                    x,
                    xText,
                    values,
                    pointsBySeries,
                    seriesOrder,
                    accumulatedValues);
            }
        }
    }

    private static void AddContinuousPoint(
        KustoResultTable table,
        bool accumulate,
        int yColumnCount,
        int yColumnIndex,
        string dimensionName,
        bool isAnomaly,
        double x,
        string xText,
        IReadOnlyList<string> values,
        Dictionary<string, List<KustoChartPointViewModel>> pointsBySeries,
        List<string> seriesOrder,
        Dictionary<string, double> accumulatedValues)
    {
        double value = yColumnIndex < 0 ? 1 : 0;
        bool hasValue = yColumnIndex < 0
            || (yColumnIndex < values.Count && TryParseNumber(values[yColumnIndex], out value));

        if (hasValue)
        {
            string yColumnName = yColumnIndex < 0 ? "Events" : table.Columns[yColumnIndex].Name;
            string seriesName = GetSeriesName(dimensionName, yColumnName, yColumnCount);
            EnsureSeries(seriesName, pointsBySeries, seriesOrder);

            if (pointsBySeries.TryGetValue(seriesName, out List<KustoChartPointViewModel>? points))
            {
                value = GetAccumulatedValue(accumulate, seriesName, value, accumulatedValues);
                points.Add(new KustoChartPointViewModel(x, xText, value, FormatNumber(value), isAnomaly));
            }
        }
    }

    private static void EnsureSeries(
        string seriesName,
        Dictionary<string, List<KustoChartPointViewModel>> pointsBySeries,
        List<string> seriesOrder)
    {
        if (!pointsBySeries.ContainsKey(seriesName) && pointsBySeries.Count < MaximumSeriesCount)
        {
            pointsBySeries.Add(seriesName, []);
            seriesOrder.Add(seriesName);
        }
    }

    private static ReadOnlyCollection<KustoChartSeriesViewModel> CreatePartitionSeries(
        KustoResultTable table,
        int categoryColumnIndex,
        int valueColumnIndex)
    {
        Dictionary<string, double> valuesByCategory = new(StringComparer.OrdinalIgnoreCase);
        List<string> categoryOrder = [];

        foreach (IReadOnlyList<string> values in table.Rows.Select(row => row.Values))
        {
            double value = 0;
            bool validRow = categoryColumnIndex < values.Count
                && valueColumnIndex < values.Count
                && TryParseNumber(values[valueColumnIndex], out value);
            if (validRow)
            {
                AddPartitionValue(values[categoryColumnIndex], value, valuesByCategory, categoryOrder);
            }
        }

        KustoChartSeriesViewModel[] series = categoryOrder
            .Take(MaximumSeriesCount)
            .Select((category, index) => new KustoChartSeriesViewModel(
                category,
                SeriesColors[index % SeriesColors.Length],
                [
                    new KustoChartPointViewModel(
                        index,
                        category,
                        valuesByCategory[category],
                        FormatNumber(valuesByCategory[category]),
                        false),
                ]))
            .ToArray();

        return Array.AsReadOnly(series);
    }

    private static void AddPartitionValue(
        string categoryText,
        double value,
        Dictionary<string, double> valuesByCategory,
        List<string> categoryOrder)
    {
        string category = string.IsNullOrWhiteSpace(categoryText) ? "(empty)" : categoryText;
        if (valuesByCategory.TryAdd(category, value))
        {
            categoryOrder.Add(category);
        }
        else
        {
            valuesByCategory[category] += value;
        }
    }

    private static KustoVisualizationViewModel CreatePlotViewModel(
        KustoResultTable table,
        KustoVisualization visualization,
        KustoChartAxisKind axisKind,
        int xColumnIndex,
        ReadOnlyCollection<KustoChartSeriesViewModel> series,
        List<string> categories)
    {
        double[] xValues = series.SelectMany(item => item.Points).Select(point => point.X).ToArray();
        double[] yValues = GetScaleValues(series, visualization);
        bool includeZero = visualization.Kind is KustoVisualizationKind.BarChart
            or KustoVisualizationKind.ColumnChart
            or KustoVisualizationKind.PieChart
            or KustoVisualizationKind.PivotChart
            or KustoVisualizationKind.TreeMap;
        AxisScale yScale = CreateNumericScale(
            yValues,
            visualization.YMinimum,
            visualization.YMaximum,
            includeZero,
            visualization.YAxisLogarithmic);
        AxisScale xScale = CreateXScale(xValues, axisKind, categories, visualization.XAxisLogarithmic);
        string xTitle = visualization.XTitle ?? table.Columns[xColumnIndex].Name;
        string yTitle = visualization.YTitle ?? "Value";
        string title = visualization.Title ?? GetDisplayName(visualization.Kind);

        return new KustoVisualizationViewModel(
            visualization,
            title,
            xTitle,
            yTitle,
            axisKind,
            xScale.Minimum,
            xScale.Maximum,
            yScale.Minimum,
            yScale.Maximum,
            xScale.Ticks,
            yScale.Ticks,
            series,
            []);
    }

    private static double[] GetScaleValues(
        ReadOnlyCollection<KustoChartSeriesViewModel> series,
        KustoVisualization visualization)
    {
        bool isStacked = visualization.Kind == KustoVisualizationKind.StackedAreaChart
            || string.Equals(visualization.KindOption, "stacked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(visualization.KindOption, "stacked100", StringComparison.OrdinalIgnoreCase);
        bool isStacked100 = string.Equals(
            visualization.KindOption,
            "stacked100",
            StringComparison.OrdinalIgnoreCase);
        double[] values;

        if (isStacked100)
        {
            values = [0, 100];
        }
        else if (isStacked)
        {
            values = series
                .SelectMany(item => item.Points)
                .GroupBy(point => point.X)
                .Select(group => group.Sum(point => Math.Max(0, point.Value)))
                .Append(0)
                .ToArray();
        }
        else
        {
            values = series.SelectMany(item => item.Points).Select(point => point.Value).ToArray();
        }

        return values;
    }

    private static AxisScale CreateXScale(
        double[] values,
        KustoChartAxisKind axisKind,
        List<string> categories,
        bool logarithmic)
    {
        AxisScale scale;

        if (axisKind == KustoChartAxisKind.Category)
        {
            double minimum = values.Length == 1 ? values[0] - 0.5 : values.Min();
            double maximum = values.Length == 1 ? values[0] + 0.5 : values.Max();
            List<KustoChartAxisTickViewModel> ticks = [];
            int stride = Math.Max(1, (int)Math.Ceiling(categories.Count / 8d));

            for (int index = 0; index < categories.Count; index += stride)
            {
                double position = Normalize(index, minimum, maximum);
                ticks.Add(new KustoChartAxisTickViewModel(position, categories[index]));
            }

            scale = new AxisScale(minimum, maximum, ticks);
        }
        else if (axisKind == KustoChartAxisKind.Time)
        {
            double minimum = values.Min();
            double maximum = values.Max();
            if (Math.Abs(maximum - minimum) < double.Epsilon)
            {
                minimum -= TimeSpan.FromMinutes(30).TotalMilliseconds;
                maximum += TimeSpan.FromMinutes(30).TotalMilliseconds;
            }

            List<KustoChartAxisTickViewModel> ticks = [];
            for (int index = 0; index < TargetTickCount; index++)
            {
                double position = index / (TargetTickCount - 1d);
                double value = minimum + ((maximum - minimum) * position);
                ticks.Add(new KustoChartAxisTickViewModel(position, FormatTimestamp(value, maximum - minimum)));
            }

            scale = new AxisScale(minimum, maximum, ticks);
        }
        else
        {
            scale = CreateNumericScale(values, null, null, false, logarithmic);
        }

        return scale;
    }

    private static AxisScale CreateNumericScale(
        double[] values,
        double? requestedMinimum,
        double? requestedMaximum,
        bool includeZero,
        bool logarithmic)
    {
        double minimum = requestedMinimum ?? values.Min();
        double maximum = requestedMaximum ?? values.Max();

        if (includeZero && !logarithmic)
        {
            minimum = Math.Min(0, minimum);
            maximum = Math.Max(0, maximum);
        }

        AxisScale scale = logarithmic && minimum > 0
            ? CreateLogarithmicScale(minimum, maximum)
            : CreateLinearScale(minimum, maximum, requestedMinimum, requestedMaximum);

        return scale;
    }

    private static AxisScale CreateLinearScale(
        double minimum,
        double maximum,
        double? requestedMinimum,
        double? requestedMaximum)
    {
        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            double padding = Math.Abs(maximum) < 1 ? 1 : Math.Abs(maximum) * 0.1;
            minimum -= padding;
            maximum += padding;
        }

        double step = NiceNumber((maximum - minimum) / (TargetTickCount - 1), true);
        double niceMinimum = requestedMinimum ?? Math.Floor(minimum / step) * step;
        double niceMaximum = requestedMaximum ?? Math.Ceiling(maximum / step) * step;
        List<KustoChartAxisTickViewModel> ticks = [];

        for (double value = niceMinimum; value <= niceMaximum + (step * 0.5); value += step)
        {
            ticks.Add(new KustoChartAxisTickViewModel(
                Normalize(value, niceMinimum, niceMaximum),
                FormatNumber(value)));
        }

        return new AxisScale(niceMinimum, niceMaximum, ticks);
    }

    private static AxisScale CreateLogarithmicScale(double minimum, double maximum)
    {
        int minimumPower = (int)Math.Floor(Math.Log10(minimum));
        int maximumPower = (int)Math.Ceiling(Math.Log10(maximum));
        double axisMinimum = Math.Pow(10, minimumPower);
        double axisMaximum = Math.Pow(10, maximumPower);
        List<KustoChartAxisTickViewModel> ticks = [];

        for (int power = minimumPower; power <= maximumPower; power++)
        {
            double value = Math.Pow(10, power);
            ticks.Add(new KustoChartAxisTickViewModel(
                Normalize(Math.Log10(value), minimumPower, maximumPower),
                FormatNumber(value)));
        }

        return new AxisScale(axisMinimum, axisMaximum, ticks);
    }

    private static int ResolveXColumnIndex(
        IReadOnlyList<KustoResultColumn> columns,
        KustoVisualization visualization)
    {
        int index = FindColumnIndex(columns, visualization.XColumn);

        if (index < 0 && visualization.Kind is KustoVisualizationKind.TimeChart
            or KustoVisualizationKind.AnomalyChart
            or KustoVisualizationKind.TimePivot)
        {
            index = FindColumnIndex(columns, IsDateTimeColumn);
        }

        if (index < 0 && visualization.Kind is KustoVisualizationKind.LineChart
            or KustoVisualizationKind.AreaChart
            or KustoVisualizationKind.ScatterChart
            or KustoVisualizationKind.StackedAreaChart)
        {
            index = FindColumnIndex(columns, column => IsDateTimeColumn(column) || IsNumericColumn(column));
        }

        if (index < 0 && columns.Count > 0 && !RequiresTimeAxis(visualization.Kind))
        {
            index = 0;
        }

        return index;
    }

    private static int[] ResolveYColumnIndexes(
        IReadOnlyList<KustoResultColumn> columns,
        KustoVisualization visualization,
        int xColumnIndex,
        int[] anomalyColumnIndexes)
    {
        int[] indexes = ResolveColumnIndexes(columns, visualization.YColumns);

        if (indexes.Length == 0)
        {
            indexes = columns
                .Select((column, index) => (Column: column, Index: index))
                .Where(item => item.Index != xColumnIndex
                    && !anomalyColumnIndexes.Contains(item.Index)
                    && IsNumericColumn(item.Column))
                .Select(item => item.Index)
                .ToArray();
        }

        if (indexes.Length == 0 && visualization.Kind == KustoVisualizationKind.TimePivot)
        {
            indexes = [-1];
        }

        if (IsPartitionVisualization(visualization.Kind) && indexes.Length > 1)
        {
            indexes = [indexes[0]];
        }

        return indexes;
    }

    private static int[] ResolveSeriesColumnIndexes(
        IReadOnlyList<KustoResultColumn> columns,
        KustoVisualization visualization,
        int xColumnIndex,
        int[] yColumnIndexes,
        int[] anomalyColumnIndexes)
    {
        int[] indexes = ResolveColumnIndexes(columns, visualization.SeriesColumns);

        if (indexes.Length == 0 && !IsPartitionVisualization(visualization.Kind))
        {
            indexes = columns
                .Select((column, index) => (Column: column, Index: index))
                .Where(item => item.Index != xColumnIndex
                    && !yColumnIndexes.Contains(item.Index)
                    && !anomalyColumnIndexes.Contains(item.Index)
                    && IsDimensionColumn(item.Column))
                .Select(item => item.Index)
                .ToArray();
        }

        return indexes;
    }

    private static int[] ResolveColumnIndexes(
        IReadOnlyList<KustoResultColumn> columns,
        IReadOnlyList<string> names)
    {
        int[] indexes = names
            .Select(name => FindColumnIndex(columns, name))
            .Where(index => index >= 0)
            .Distinct()
            .ToArray();

        return indexes;
    }

    private static int FindColumnIndex(IReadOnlyList<KustoResultColumn> columns, string? name)
    {
        int index = -1;

        if (!string.IsNullOrWhiteSpace(name))
        {
            index = FindColumnIndex(columns, column => string.Equals(
                column.Name,
                name,
                StringComparison.OrdinalIgnoreCase));
        }

        return index;
    }

    private static int FindColumnIndex(
        IReadOnlyList<KustoResultColumn> columns,
        Func<KustoResultColumn, bool> predicate)
    {
        int matchingIndex = -1;

        for (int index = 0; index < columns.Count && matchingIndex < 0; index++)
        {
            if (predicate(columns[index]))
            {
                matchingIndex = index;
            }
        }

        return matchingIndex;
    }

    private static bool TryGetXValue(
        IReadOnlyList<string> values,
        int xColumnIndex,
        KustoChartAxisKind axisKind,
        List<string> categories,
        out double x,
        out string xText)
    {
        x = 0;
        xText = string.Empty;
        bool success = xColumnIndex < values.Count;

        if (success)
        {
            xText = string.IsNullOrWhiteSpace(values[xColumnIndex]) ? "(empty)" : values[xColumnIndex];
            if (axisKind == KustoChartAxisKind.Time)
            {
                success = DateTimeOffset.TryParse(
                    values[xColumnIndex],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset timestamp);
                x = timestamp.ToUnixTimeMilliseconds();
            }
            else if (axisKind == KustoChartAxisKind.Numeric)
            {
                success = TryParseNumber(values[xColumnIndex], out x);
            }
            else
            {
                int categoryIndex = GetCategoryIndex(categories, xText);
                if (categoryIndex < 0)
                {
                    categoryIndex = categories.Count;
                    categories.Add(xText);
                }

                x = categoryIndex;
            }
        }

        return success;
    }

    private static int GetCategoryIndex(List<string> categories, string categoryText)
    {
        int categoryIndex = -1;

        for (int index = 0; index < categories.Count && categoryIndex < 0; index++)
        {
            if (string.Equals(categories[index], categoryText, StringComparison.Ordinal))
            {
                categoryIndex = index;
            }
        }

        return categoryIndex;
    }

    private static string GetDimensionName(IReadOnlyList<string> values, int[] seriesColumnIndexes)
    {
        string dimensionName = string.Join(" · ", seriesColumnIndexes.Select(index => GetDimensionValue(values, index)));

        return dimensionName;
    }

    private static string GetDimensionValue(IReadOnlyList<string> values, int index)
    {
        string value = "(empty)";

        if (index < values.Count && !string.IsNullOrWhiteSpace(values[index]))
        {
            value = values[index];
        }

        return value;
    }

    private static string GetSeriesName(string dimensionName, string yColumnName, int yColumnCount)
    {
        string name = yColumnName;

        if (!string.IsNullOrWhiteSpace(dimensionName))
        {
            name = yColumnCount == 1 ? dimensionName : $"{dimensionName} · {yColumnName}";
        }

        return name;
    }

    private static double GetAccumulatedValue(
        bool accumulate,
        string seriesName,
        double value,
        Dictionary<string, double> accumulatedValues)
    {
        double result = value;

        if (accumulate)
        {
            accumulatedValues.TryGetValue(seriesName, out double previousValue);
            result += previousValue;
            accumulatedValues[seriesName] = result;
        }

        return result;
    }

    private static bool IsAnomaly(IReadOnlyList<string> values, int[] anomalyColumnIndexes)
    {
        bool isAnomaly = anomalyColumnIndexes.Any(index => index < values.Count
            && TryParseNumber(values[index], out double value)
            && Math.Abs(value) > double.Epsilon);

        return isAnomaly;
    }

    private static KustoChartAxisKind GetAxisKind(KustoResultColumn column)
    {
        KustoChartAxisKind kind = KustoChartAxisKind.Category;

        if (IsDateTimeColumn(column))
        {
            kind = KustoChartAxisKind.Time;
        }
        else if (IsNumericColumn(column))
        {
            kind = KustoChartAxisKind.Numeric;
        }

        return kind;
    }

    private static bool IsDateTimeColumn(KustoResultColumn column)
    {
        return column.TypeName.Contains("datetime", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDimensionColumn(KustoResultColumn column)
    {
        string typeName = column.TypeName;
        return typeName.Contains("string", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("bool", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("guid", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumericColumn(KustoResultColumn column)
    {
        string typeName = column.TypeName;
        return typeName.Contains("byte", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("decimal", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("double", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("float", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("int", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("long", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("real", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("single", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPartitionVisualization(KustoVisualizationKind kind)
    {
        return kind is KustoVisualizationKind.PieChart or KustoVisualizationKind.TreeMap;
    }

    private static bool RequiresTimeAxis(KustoVisualizationKind kind)
    {
        return kind is KustoVisualizationKind.AnomalyChart
            or KustoVisualizationKind.TimeChart
            or KustoVisualizationKind.TimePivot;
    }

    private static bool TryParseNumber(string text, out double value)
    {
        return double.TryParse(
            text,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value)
            && double.IsFinite(value);
    }

    private static double NiceNumber(double range, bool round)
    {
        double exponent = Math.Floor(Math.Log10(range));
        double fraction = range / Math.Pow(10, exponent);
        double niceFraction;

        if (round)
        {
            niceFraction = GetRoundedNiceFraction(fraction);
        }
        else
        {
            niceFraction = GetCeilingNiceFraction(fraction);
        }

        return niceFraction * Math.Pow(10, exponent);
    }

    private static double GetRoundedNiceFraction(double fraction)
    {
        double niceFraction = 10;

        if (fraction < 1.5)
        {
            niceFraction = 1;
        }
        else if (fraction < 3)
        {
            niceFraction = 2;
        }
        else if (fraction < 7)
        {
            niceFraction = 5;
        }

        return niceFraction;
    }

    private static double GetCeilingNiceFraction(double fraction)
    {
        double niceFraction = 10;

        if (fraction <= 1)
        {
            niceFraction = 1;
        }
        else if (fraction <= 2)
        {
            niceFraction = 2;
        }
        else if (fraction <= 5)
        {
            niceFraction = 5;
        }

        return niceFraction;
    }

    private static double Normalize(double value, double minimum, double maximum)
    {
        double range = maximum - minimum;
        double normalized = 0.5;
        if (Math.Abs(range) >= double.Epsilon)
        {
            normalized = (value - minimum) / range;
        }

        return Math.Clamp(normalized, 0, 1);
    }

    private static string FormatTimestamp(double unixMilliseconds, double spanMilliseconds)
    {
        DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)unixMilliseconds);
        TimeSpan span = TimeSpan.FromMilliseconds(spanMilliseconds);
        string format = "HH:mm:ss";

        if (span.TotalDays >= 365)
        {
            format = "yyyy-MM";
        }
        else if (span.TotalDays >= 2)
        {
            format = "MMM d";
        }
        else if (span.TotalHours >= 2)
        {
            format = "HH:mm";
        }

        return timestamp.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string GetDisplayName(KustoVisualizationKind kind)
    {
        string name = kind switch
        {
            KustoVisualizationKind.AnomalyChart => "Anomaly chart",
            KustoVisualizationKind.AreaChart => "Area chart",
            KustoVisualizationKind.BarChart => "Bar chart",
            KustoVisualizationKind.Card => "Card",
            KustoVisualizationKind.ColumnChart => "Column chart",
            KustoVisualizationKind.LadderChart => "Ladder chart",
            KustoVisualizationKind.LineChart => "Line chart",
            KustoVisualizationKind.PieChart => "Pie chart",
            KustoVisualizationKind.PivotChart => "Pivot chart",
            KustoVisualizationKind.ScatterChart => "Scatter chart",
            KustoVisualizationKind.StackedAreaChart => "Stacked area chart",
            KustoVisualizationKind.Table => "Table",
            KustoVisualizationKind.TimeChart => "Time chart",
            KustoVisualizationKind.TimePivot => "Time pivot",
            KustoVisualizationKind.TreeMap => "Treemap",
            _ => "Visualization",
        };

        return name;
    }

    private static string GetMissingRoleMessage(
        KustoVisualizationKind kind,
        int xColumnIndex,
        int yColumnCount)
    {
        string message = string.Empty;

        if (xColumnIndex < 0)
        {
            message = $"{GetDisplayName(kind)} needs an x-axis column.";
        }
        else if (yColumnCount == 0)
        {
            message = $"{GetDisplayName(kind)} needs at least one numeric value column.";
        }

        return message;
    }

    private void ToggleAllSeriesVisibility()
    {
        bool makeVisible = !AreAllSeriesVisible;

        foreach (KustoChartSeriesViewModel chartSeries in Series)
        {
            chartSeries.IsVisible = makeVisible;
        }
    }

    private void OnSeriesPropertyChanged(object? sender, PropertyChangedEventArgs eventArguments)
    {
        if (eventArguments.PropertyName == nameof(KustoChartSeriesViewModel.IsVisible))
        {
            VisibleSeries.Clear();

            foreach (KustoChartSeriesViewModel chartSeries in Series.Where(series => series.IsVisible))
            {
                VisibleSeries.Add(chartSeries);
            }

            Revision++;
            OnPropertyChanged(nameof(AreAllSeriesVisible));
            OnPropertyChanged(nameof(AllSeriesVisibilityActionText));
        }
    }

    private sealed class AxisScale
    {
        public AxisScale(double minimum, double maximum, IEnumerable<KustoChartAxisTickViewModel> ticks)
        {
            Minimum = minimum;
            Maximum = maximum;
            Ticks = Array.AsReadOnly(ticks.ToArray());
        }

        public double Minimum { get; }

        public double Maximum { get; }

        public IReadOnlyList<KustoChartAxisTickViewModel> Ticks { get; }
    }
}
