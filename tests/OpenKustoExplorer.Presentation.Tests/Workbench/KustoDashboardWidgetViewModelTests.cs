using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Workbench;

/// <summary>
/// Verifies query-backed dashboard widget behavior.
/// </summary>
public sealed class KustoDashboardWidgetViewModelTests
{
    /// <summary>
    /// Verifies results are projected once per elapsed refresh interval.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshIfDueProjectsVisualizationAndWaitsForInterval()
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [
                new KustoResultColumn("Service", "string"),
                new KustoResultColumn("Count", "long"),
            ],
            [
                new KustoResultRow(["API", "12"]),
                new KustoResultRow(["Worker", "7"]),
            ]);
        StubKustoQueryService queryService = new()
        {
            Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(42)),
        };
        KustoDashboardWidget definition = new(
            Guid.NewGuid(),
            "Errors by service",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Errors | summarize Count=count() by Service",
            TimeSpan.FromMinutes(2),
            KustoDashboardWidgetDisplayMode.Visualization,
            KustoVisualizationKind.ColumnChart,
            new KustoDashboardWidgetLayout(2, 3, 12, 8),
            "#FFFFFF",
            "#202124",
            "#D64545");
        using KustoDashboardWidgetViewModel viewModel = new(definition, queryService);
        DateTimeOffset startedAtUtc = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        await viewModel.RefreshIfDueAsync(startedAtUtc);
        await viewModel.RefreshIfDueAsync(startedAtUtc.AddMinutes(1));

        Assert.Equal(1, queryService.ExecuteCount);
        Assert.Equal(definition.QueryText, queryService.Request?.QueryText);
        Assert.Equal(2, viewModel.ResultColumns.Count);
        Assert.Equal(2, viewModel.ResultRows.Count);
        Assert.NotNull(viewModel.Visualization);
        Assert.Equal(KustoVisualizationKind.ColumnChart, viewModel.Visualization.Kind);
        Assert.Equal(startedAtUtc.AddMinutes(2), viewModel.NextRefreshAtUtc);

        await viewModel.RefreshIfDueAsync(startedAtUtc.AddMinutes(2));

        Assert.Equal(2, queryService.ExecuteCount);
    }

    private sealed class StubKustoQueryService : IKustoQueryService
    {
        public int ExecuteCount { get; private set; }

        public KustoQueryRequest? Request { get; private set; }

        public required KustoQueryResult Result { get; init; }

        public Task<KustoQueryResult> ExecuteAsync(
            KustoQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            Request = request;
            return Task.FromResult(Result);
        }
    }
}
