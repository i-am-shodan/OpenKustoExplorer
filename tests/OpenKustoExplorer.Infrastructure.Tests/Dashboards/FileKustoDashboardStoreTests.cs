using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Infrastructure.Dashboards;

namespace OpenKustoExplorer.Infrastructure.Tests.Dashboards;

/// <summary>
/// Verifies durable dashboard persistence and portable JSON exchange.
/// </summary>
public sealed class FileKustoDashboardStoreTests
{
    /// <summary>
    /// Verifies widget queries, refresh, visualization, layout, and colors round-trip through JSON.
    /// </summary>
    /// <returns>A task that completes after the catalog is exchanged.</returns>
    [Fact]
    public async Task SaveLoadAndExchangeRoundTripDashboard()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "dashboards.json");
        DateTimeOffset cachedAtUtc = new(2026, 9, 8, 10, 30, 0, TimeSpan.Zero);
        DateTimeOffset rangeStartUtc = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset rangeEndUtc = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        KustoQueryResult cachedResult = new(
            [
                new KustoResultTable(
                    "Results",
                    [
                        new KustoResultColumn("Service", "string"),
                        new KustoResultColumn("Count", "long"),
                        new KustoResultColumn("Optional", "string"),
                    ],
                    [
                        new KustoResultRow(
                        [
                            new KustoResultValue("API", "\"API\"", false),
                            new KustoResultValue("12", "12", false),
                            new KustoResultValue(string.Empty, "\"\"", false),
                        ]),
                    ]),
            ],
            TimeSpan.FromMilliseconds(42));
        KustoDashboardWidget widget = new(
            Guid.NewGuid(),
            "Errors by service",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Errors | summarize Count=count() by Service",
            TimeSpan.FromMinutes(2),
            KustoDashboardWidgetDisplayMode.Visualization,
            KustoVisualizationKind.ColumnChart,
            new KustoDashboardWidgetLayout(3, 5, 14, 9),
            "#FFFDF7",
            "#202124",
            "#D64545",
            cachedResult,
            cachedAtUtc);
        KustoDashboard dashboard = new(
            Guid.NewGuid(),
            "Operations",
            "#EEF2F3",
            [widget],
            KustoDashboardTimeRange.CreateAbsolute(rangeStartUtc, rangeEndUtc));

        try
        {
            FileKustoDashboardStore store = new(filePath);
            await store.SaveAsync(new KustoDashboardCatalog([dashboard]));

            KustoDashboard restored = Assert.Single(store.Load().Dashboards);
            using MemoryStream stream = new();
            store.Export(stream, restored);
            stream.Position = 0;
            KustoDashboard imported = store.Import(stream);
            KustoDashboardWidget importedWidget = Assert.Single(imported.Widgets);

            Assert.Equal(dashboard.Id, imported.Id);
            Assert.Equal("Operations", imported.Title);
            Assert.Equal("#EEF2F3", imported.BackgroundColor);
            Assert.Equal(KustoDashboardTimeRangeKind.Absolute, imported.TimeRange.Kind);
            Assert.Equal(rangeStartUtc, imported.TimeRange.StartUtc);
            Assert.Equal(rangeEndUtc, imported.TimeRange.EndUtc);
            Assert.Equal(widget.Id, importedWidget.Id);
            Assert.Equal("Errors | summarize Count=count() by Service", importedWidget.QueryText);
            Assert.Equal(TimeSpan.FromMinutes(2), importedWidget.RefreshInterval);
            Assert.Equal(KustoVisualizationKind.ColumnChart, importedWidget.VisualizationKind);
            Assert.Equal(3, importedWidget.Layout.Column);
            Assert.Equal(5, importedWidget.Layout.Row);
            Assert.Equal(14, importedWidget.Layout.ColumnSpan);
            Assert.Equal(9, importedWidget.Layout.RowSpan);
            Assert.Equal("#FFFDF7", importedWidget.BackgroundColor);
            Assert.Equal("#202124", importedWidget.ForegroundColor);
            Assert.Equal("#D64545", importedWidget.AccentColor);
            Assert.Equal(cachedAtUtc, importedWidget.CachedAtUtc);
            KustoQueryResult importedResult = Assert.IsType<KustoQueryResult>(importedWidget.CachedResult);
            KustoResultRow importedRow = Assert.Single(Assert.Single(importedResult.Tables).Rows);
            Assert.Equal(["API", "12", string.Empty], importedRow.Values);
            Assert.Equal("\"API\"", importedRow.ResultValues[0].RawJson);
            Assert.Equal("12", importedRow.ResultValues[1].RawJson);
            Assert.Equal("\"\"", importedRow.ResultValues[2].RawJson);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }

    /// <summary>
    /// Verifies version 1 dashboards receive the Last 24 hours default.
    /// </summary>
    [Fact]
    public void LoadVersionOneDefaultsToLast24Hours()
    {
        string directoryPath = Path.Join(Path.GetTempPath(), $"OpenKustoExplorer-{Guid.NewGuid():N}");
        string filePath = Path.Join(directoryPath, "dashboards.json");
        Guid dashboardId = Guid.NewGuid();

        try
        {
            Directory.CreateDirectory(directoryPath);
            string catalogJson = $$"""{"version":1,"dashboards":[{"id":"{{dashboardId}}","title":"Legacy","backgroundColor":"#FFFFFF","widgets":[]}]}""";
            File.WriteAllText(filePath, catalogJson);

            KustoDashboard restored = Assert.Single(new FileKustoDashboardStore(filePath).Load().Dashboards);

            Assert.Equal(dashboardId, restored.Id);
            Assert.Equal(KustoDashboardTimeRangeKind.Relative, restored.TimeRange.Kind);
            Assert.Equal(TimeSpan.FromHours(24), restored.TimeRange.RelativeDuration);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }

    /// <summary>
    /// Verifies version 2 imports require an explicit dashboard time range.
    /// </summary>
    [Fact]
    public void ImportVersionTwoRejectsMissingTimeRange()
    {
        string json = $$"""{"version":2,"dashboards":[{"id":"{{Guid.NewGuid()}}","title":"Invalid","backgroundColor":"#FFFFFF","widgets":[]}]}""";
        using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(json));
        FileKustoDashboardStore store = new(Path.Join(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.json"));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => store.Import(stream));

        Assert.Contains("timeRange", exception.Message, StringComparison.Ordinal);
    }
}
