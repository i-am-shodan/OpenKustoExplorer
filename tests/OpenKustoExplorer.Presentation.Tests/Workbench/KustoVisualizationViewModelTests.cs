using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Workbench;

/// <summary>
/// Verifies Kusto-compatible visualization role and series inference.
/// </summary>
public sealed class KustoVisualizationViewModelTests
{
    /// <summary>
    /// Verifies that an unused text column becomes a dimension with one time series per distinct value.
    /// </summary>
    [Fact]
    public void TimeChartInfersSeriesFromProtocolValues()
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [
                new KustoResultColumn("Protocol", "string"),
                new KustoResultColumn("count_", "long"),
                new KustoResultColumn("FirstConnection", "datetime"),
            ],
            [
                new KustoResultRow(["TCP", "12", "2026-07-21T00:00:00Z"]),
                new KustoResultRow(["UDP", "7", "2026-07-21T00:00:00Z"]),
                new KustoResultRow(["TCP", "18", "2026-07-21T04:00:00Z"]),
                new KustoResultRow(["UDP", "9", "2026-07-21T04:00:00Z"]),
            ]);

        bool created = KustoVisualizationViewModel.TryCreate(
            table,
            new KustoVisualization(KustoVisualizationKind.TimeChart),
            out KustoVisualizationViewModel? visualization,
            out string message);

        Assert.True(created, message);
        Assert.NotNull(visualization);
        Assert.Equal(KustoChartAxisKind.Time, visualization.XAxisKind);
        Assert.Equal(["TCP", "UDP"], visualization.Series.Select(series => series.Name));
        Assert.All(visualization.Series, series => Assert.Equal(2, series.Points.Count));
        Assert.Equal("FirstConnection", visualization.XAxisTitle);
    }

    /// <summary>
    /// Verifies continuous render types honor explicit x, y, and series roles.
    /// </summary>
    /// <param name="kind">The Kusto visualization kind.</param>
    [Theory]
    [InlineData(KustoVisualizationKind.AreaChart)]
    [InlineData(KustoVisualizationKind.BarChart)]
    [InlineData(KustoVisualizationKind.ColumnChart)]
    [InlineData(KustoVisualizationKind.LadderChart)]
    [InlineData(KustoVisualizationKind.LineChart)]
    [InlineData(KustoVisualizationKind.PivotChart)]
    [InlineData(KustoVisualizationKind.ScatterChart)]
    [InlineData(KustoVisualizationKind.StackedAreaChart)]
    public void ContinuousVisualizationKindsCreateSeries(KustoVisualizationKind kind)
    {
        KustoResultTable table = CreateContinuousTable();
        KustoVisualization instructions = new(
            kind,
            xColumn: "Bucket",
            seriesColumns: ["Region"],
            yColumns: ["Value"]);

        bool created = KustoVisualizationViewModel.TryCreate(
            table,
            instructions,
            out KustoVisualizationViewModel? visualization,
            out string message);

        Assert.True(created, message);
        Assert.NotNull(visualization);
        Assert.Equal(kind, visualization.Kind);
        Assert.Equal(["East", "West"], visualization.Series.Select(series => series.Name));
    }

    /// <summary>
    /// Verifies time-oriented Kusto render types use a datetime x-axis.
    /// </summary>
    /// <param name="kind">The time-oriented visualization kind.</param>
    [Theory]
    [InlineData(KustoVisualizationKind.AnomalyChart)]
    [InlineData(KustoVisualizationKind.TimeChart)]
    [InlineData(KustoVisualizationKind.TimePivot)]
    public void TimeVisualizationKindsCreateTimeSeries(KustoVisualizationKind kind)
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [
                new KustoResultColumn("Timestamp", "datetime"),
                new KustoResultColumn("Service", "string"),
                new KustoResultColumn("Requests", "long"),
            ],
            [
                new KustoResultRow(["2026-07-21T00:00:00Z", "API", "100"]),
                new KustoResultRow(["2026-07-21T01:00:00Z", "API", "120"]),
            ]);

        bool created = KustoVisualizationViewModel.TryCreate(
            table,
            new KustoVisualization(kind),
            out KustoVisualizationViewModel? visualization,
            out string message);

        Assert.True(created, message);
        Assert.NotNull(visualization);
        Assert.Equal(KustoChartAxisKind.Time, visualization.XAxisKind);
    }

    /// <summary>
    /// Verifies that timepivot can display timestamped events without a numeric measure.
    /// </summary>
    [Fact]
    public void TimePivotSynthesizesEventMarkersWithoutNumericColumn()
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Timestamp", "datetime"), new KustoResultColumn("EventType", "string")],
            [
                new KustoResultRow(["2026-07-21T00:00:00Z", "Started"]),
                new KustoResultRow(["2026-07-21T01:00:00Z", "Stopped"]),
            ]);

        bool created = KustoVisualizationViewModel.TryCreate(
            table,
            new KustoVisualization(KustoVisualizationKind.TimePivot),
            out KustoVisualizationViewModel? visualization,
            out string message);

        Assert.True(created, message);
        Assert.NotNull(visualization);
        Assert.Equal(["Started", "Stopped"], visualization.Series.Select(series => series.Name));
        Assert.All(visualization.Series, series => Assert.Equal(1, series.Points[0].Value));
    }

    /// <summary>
    /// Verifies pie and treemap visualizations aggregate repeated category values.
    /// </summary>
    /// <param name="kind">The partition visualization kind.</param>
    [Theory]
    [InlineData(KustoVisualizationKind.PieChart)]
    [InlineData(KustoVisualizationKind.TreeMap)]
    public void PartitionVisualizationKindsAggregateCategories(KustoVisualizationKind kind)
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Protocol", "string"), new KustoResultColumn("Events", "long")],
            [
                new KustoResultRow(["TCP", "10"]),
                new KustoResultRow(["UDP", "7"]),
                new KustoResultRow(["TCP", "5"]),
            ]);

        bool created = KustoVisualizationViewModel.TryCreate(
            table,
            new KustoVisualization(kind),
            out KustoVisualizationViewModel? visualization,
            out string message);

        Assert.True(created, message);
        Assert.NotNull(visualization);
        Assert.Equal(["TCP", "UDP"], visualization.Series.Select(series => series.Name));
        Assert.Equal(15, visualization.Series[0].Points[0].Value);
    }

    /// <summary>
    /// Verifies card visualizations expose every scalar in the first result row.
    /// </summary>
    [Fact]
    public void CardVisualizationUsesFirstResultRow()
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Total", "long"), new KustoResultColumn("State", "string")],
            [new KustoResultRow(["42", "Healthy"])]);

        bool created = KustoVisualizationViewModel.TryCreate(
            table,
            new KustoVisualization(KustoVisualizationKind.Card),
            out KustoVisualizationViewModel? visualization,
            out string message);

        Assert.True(created, message);
        Assert.NotNull(visualization);
        Assert.True(visualization.IsCard);
        Assert.Equal(["Total", "State"], visualization.Cards.Select(card => card.Label));
    }

    /// <summary>
    /// Verifies large values use compact human-readable units on chart scales.
    /// </summary>
    [Fact]
    public void NumericScaleUsesHumanReadableLabels()
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Bucket", "long"), new KustoResultColumn("Value", "long")],
            [new KustoResultRow(["1", "1250000"]), new KustoResultRow(["2", "2750000"])]);
        KustoVisualization instructions = new(
            KustoVisualizationKind.LineChart,
            xColumn: "Bucket",
            yColumns: ["Value"]);

        bool created = KustoVisualizationViewModel.TryCreate(
            table,
            instructions,
            out KustoVisualizationViewModel? visualization,
            out string message);

        Assert.True(created, message);
        Assert.NotNull(visualization);
        Assert.Contains(visualization.YAxisTicks, tick => tick.Label.Contains('M', StringComparison.Ordinal));
        Assert.Equal("1.25M", KustoVisualizationViewModel.FormatNumber(1_250_000));
        Assert.Equal("3.5B", KustoVisualizationViewModel.FormatNumber(3_500_000_000));
    }

    /// <summary>
    /// Verifies that legend actions toggle rendered series and increment the redraw revision.
    /// </summary>
    [Fact]
    public void LegendCommandTogglesSeriesVisibility()
    {
        KustoVisualizationViewModel.TryCreate(
            CreateContinuousTable(),
            new KustoVisualization(
                KustoVisualizationKind.LineChart,
                xColumn: "Bucket",
                seriesColumns: ["Region"],
                yColumns: ["Value"]),
            out KustoVisualizationViewModel? visualization,
            out string message);
        Assert.NotNull(visualization);
        KustoChartSeriesViewModel firstSeries = visualization.Series[0];

        firstSeries.ToggleVisibilityCommand.Execute(null);

        Assert.False(firstSeries.IsVisible, message);
        Assert.DoesNotContain(firstSeries, visualization.VisibleSeries);
        Assert.Equal(1, visualization.Revision);

        firstSeries.ToggleVisibilityCommand.Execute(null);

        Assert.True(firstSeries.IsVisible);
        Assert.Contains(firstSeries, visualization.VisibleSeries);
        Assert.Equal(2, visualization.Revision);
    }

    /// <summary>
    /// Verifies the chart-wide visibility command hides and then restores every series.
    /// </summary>
    [Fact]
    public void ToggleAllSeriesVisibilityCommandHidesAndShowsEverySeries()
    {
        KustoVisualizationViewModel.TryCreate(
            CreateContinuousTable(),
            new KustoVisualization(
                KustoVisualizationKind.LineChart,
                xColumn: "Bucket",
                seriesColumns: ["Region"],
                yColumns: ["Value"]),
            out KustoVisualizationViewModel? visualization,
            out string message);
        Assert.NotNull(visualization);
        Assert.True(visualization.AreAllSeriesVisible);
        Assert.Equal("Hide all series", visualization.AllSeriesVisibilityActionText);

        visualization.ToggleAllSeriesVisibilityCommand.Execute(null);

        Assert.All(visualization.Series, series => Assert.False(series.IsVisible, message));
        Assert.Empty(visualization.VisibleSeries);
        Assert.False(visualization.AreAllSeriesVisible);
        Assert.Equal("Show all series", visualization.AllSeriesVisibilityActionText);
        Assert.True(visualization.ToggleAllSeriesVisibilityCommand.CanExecute(null));

        visualization.ToggleAllSeriesVisibilityCommand.Execute(null);

        Assert.All(visualization.Series, series => Assert.True(series.IsVisible));
        Assert.Equal(visualization.Series, visualization.VisibleSeries);
        Assert.True(visualization.AreAllSeriesVisible);
        Assert.Equal("Hide all series", visualization.AllSeriesVisibilityActionText);
    }

    /// <summary>
    /// Verifies the chart-wide visibility command restores all series from a mixed visibility state.
    /// </summary>
    [Fact]
    public void ToggleAllSeriesVisibilityCommandShowsAllFromMixedState()
    {
        KustoVisualizationViewModel.TryCreate(
            CreateContinuousTable(),
            new KustoVisualization(
                KustoVisualizationKind.LineChart,
                xColumn: "Bucket",
                seriesColumns: ["Region"],
                yColumns: ["Value"]),
            out KustoVisualizationViewModel? visualization,
            out string message);
        Assert.NotNull(visualization);

        KustoChartSeriesViewModel firstSeries = visualization.Series[0];
        firstSeries.ToggleVisibilityCommand.Execute(null);

        Assert.False(firstSeries.IsVisible, message);
        Assert.Equal("Show all series", visualization.AllSeriesVisibilityActionText);

        visualization.ToggleAllSeriesVisibilityCommand.Execute(null);

        Assert.All(visualization.Series, series => Assert.True(series.IsVisible));
        Assert.Equal(visualization.Series, visualization.VisibleSeries);
    }

    private static KustoResultTable CreateContinuousTable()
    {
        return new KustoResultTable(
            "PrimaryResult",
            [
                new KustoResultColumn("Bucket", "double"),
                new KustoResultColumn("Region", "string"),
                new KustoResultColumn("Value", "double"),
            ],
            [
                new KustoResultRow(["1", "East", "10"]),
                new KustoResultRow(["1", "West", "8"]),
                new KustoResultRow(["2", "East", "14"]),
                new KustoResultRow(["2", "West", "12"]),
            ]);
    }
}
