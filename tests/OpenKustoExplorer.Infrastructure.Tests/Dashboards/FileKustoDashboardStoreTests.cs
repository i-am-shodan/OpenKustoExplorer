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
    [Fact]
    public void SaveLoadAndExchangeRoundTripDashboard()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "dashboards.json");
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
            "#D64545");
        KustoDashboard dashboard = new(
            Guid.NewGuid(),
            "Operations",
            "#EEF2F3",
            [widget]);

        try
        {
            FileKustoDashboardStore store = new(filePath);
            store.Save(new KustoDashboardCatalog([dashboard]));

            KustoDashboard restored = Assert.Single(store.Load().Dashboards);
            using MemoryStream stream = new();
            store.Export(stream, restored);
            stream.Position = 0;
            KustoDashboard imported = store.Import(stream);
            KustoDashboardWidget importedWidget = Assert.Single(imported.Widgets);

            Assert.Equal(dashboard.Id, imported.Id);
            Assert.Equal("Operations", imported.Title);
            Assert.Equal("#EEF2F3", imported.BackgroundColor);
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
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }
}
