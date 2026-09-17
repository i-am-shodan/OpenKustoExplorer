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
        KustoDashboardWidget? persistedDefinition = null;
        using KustoDashboardWidgetViewModel viewModel = new(
            definition,
            queryService,
            widget => persistedDefinition = widget.CreateDefinition());
        DateTimeOffset startedAtUtc = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        await viewModel.RefreshIfDueAsync(startedAtUtc);
        await viewModel.RefreshIfDueAsync(startedAtUtc.AddMinutes(1));

        Assert.Equal(1, queryService.ExecuteCount);
        Assert.Equal(
            "let _startTime = datetime(2026-01-01T03:04:05.0000000Z);\n"
            + "let _endTime = datetime(2026-01-02T03:04:05.0000000Z);\n"
            + definition.QueryText,
            queryService.Request?.QueryText);
        Assert.Equal(2, viewModel.ResultColumns.Count);
        Assert.Equal(2, viewModel.ResultRows.Count);
        Assert.NotNull(viewModel.Visualization);
        Assert.Equal(KustoVisualizationKind.ColumnChart, viewModel.Visualization.Kind);
        Assert.Equal(startedAtUtc.AddMinutes(2), viewModel.NextRefreshAtUtc);
        Assert.NotNull(persistedDefinition?.CachedResult);
        Assert.Equal(startedAtUtc, persistedDefinition.CachedAtUtc);

        await viewModel.RefreshIfDueAsync(startedAtUtc.AddMinutes(2));

        Assert.Equal(2, queryService.ExecuteCount);
    }

    /// <summary>
    /// Verifies a fresh persisted result renders immediately and executes only after expiry.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FreshCachedResultHydratesAndWaitsUntilExpiry()
    {
        DateTimeOffset cachedAtUtc = DateTimeOffset.UtcNow;
        KustoResultTable table = new(
            "Results",
            [new KustoResultColumn("Service", "string")],
            [new KustoResultRow(["API"])]);
        KustoQueryResult cachedResult = new([table], TimeSpan.FromMilliseconds(18));
        StubKustoQueryService queryService = new() { Result = cachedResult };
        KustoDashboardWidget definition = new(
            Guid.NewGuid(),
            "Cached services",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Services | take 10",
            TimeSpan.FromMinutes(2),
            KustoDashboardWidgetDisplayMode.Table,
            KustoVisualizationKind.Table,
            new KustoDashboardWidgetLayout(0, 0, 12, 8),
            "#FFFFFF",
            "#202124",
            "#1769AA",
            cachedResult,
            cachedAtUtc);
        using KustoDashboardWidgetViewModel viewModel = new(definition, queryService);

        await viewModel.RefreshIfDueAsync(cachedAtUtc.AddMinutes(1));

        Assert.Equal(0, queryService.ExecuteCount);
        Assert.Equal("API", Assert.Single(viewModel.ResultRows).Cells[0].Text);
        Assert.Equal(cachedAtUtc, viewModel.LastRefreshedAtUtc);
        Assert.Equal(cachedAtUtc.AddMinutes(2), viewModel.NextRefreshAtUtc);

        await viewModel.RefreshIfDueAsync(cachedAtUtc.AddMinutes(2));

        Assert.Equal(1, queryService.ExecuteCount);
        Assert.NotNull(viewModel.CreateDefinition().CachedResult);
        Assert.Equal(cachedAtUtc.AddMinutes(2), viewModel.CreateDefinition().CachedAtUtc);
    }

    /// <summary>
    /// Verifies the owning dashboard range is used by an individual widget refresh.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IndividualRefreshUsesOwningDashboardRange()
    {
        StubKustoQueryService queryService = new()
        {
            Result = new KustoQueryResult([], TimeSpan.Zero),
        };
        KustoDashboardWidget definition = new(
            Guid.NewGuid(),
            "Errors",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Errors | where Timestamp between (_startTime.._endTime)",
            TimeSpan.FromMinutes(2),
            KustoDashboardWidgetDisplayMode.Table,
            KustoVisualizationKind.Table,
            new KustoDashboardWidgetLayout(0, 0, 12, 8),
            "#FFFFFF",
            "#202124",
            "#1769AA");
        using KustoDashboardWidgetViewModel viewModel = new(
            definition,
            queryService,
            timeRangeResolver: utcNow => KustoDashboardTimeRange
                .CreateRelative(TimeSpan.FromHours(6))
                .Resolve(utcNow));
        DateTimeOffset utcNow = new(2026, 9, 16, 18, 0, 0, TimeSpan.Zero);

        await viewModel.RefreshAsync(utcNow);

        Assert.Equal(
            "let _startTime = datetime(2026-09-16T12:00:00.0000000Z);\n"
            + "let _endTime = datetime(2026-09-16T18:00:00.0000000Z);\n"
            + definition.QueryText,
            queryService.Request?.QueryText);
        Assert.Equal(definition.QueryText, viewModel.QueryText);
    }

    /// <summary>
    /// Verifies a late superseded refresh cannot overwrite a newer result.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SupersededRefreshCannotApplyLateResult()
    {
        ControlledKustoQueryService queryService = new();
        KustoDashboardWidget definition = new(
            Guid.NewGuid(),
            "Services",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Services | take 1",
            TimeSpan.FromMinutes(2),
            KustoDashboardWidgetDisplayMode.Table,
            KustoVisualizationKind.Table,
            new KustoDashboardWidgetLayout(0, 0, 12, 8),
            "#FFFFFF",
            "#202124",
            "#1769AA");
        using KustoDashboardWidgetViewModel viewModel = new(definition, queryService);
        DateTimeOffset firstTime = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset secondTime = firstTime.AddMinutes(1);

        Task firstRefresh = viewModel.RefreshAsync(firstTime);
        Task secondRefresh = viewModel.RefreshAsync(secondTime);
        Assert.Equal(2, queryService.Completions.Count);
        queryService.Completions[1].SetResult(CreateSingleValueResult("New"));
        await secondRefresh;
        queryService.Completions[0].SetResult(CreateSingleValueResult("Old"));
        await firstRefresh;

        Assert.Equal("New", Assert.Single(viewModel.ResultRows).Cells[0].Text);
        Assert.Equal(secondTime, viewModel.LastRefreshedAtUtc);
        Assert.Equal(secondTime, viewModel.CreateDefinition().CachedAtUtc);
    }

    private static KustoQueryResult CreateSingleValueResult(string value)
    {
        KustoResultTable table = new(
            "Results",
            [new KustoResultColumn("Value", "string")],
            [new KustoResultRow([value])]);
        return new KustoQueryResult([table], TimeSpan.Zero);
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

    private sealed class ControlledKustoQueryService : IKustoQueryService
    {
        public List<TaskCompletionSource<KustoQueryResult>> Completions { get; } = [];

        public Task<KustoQueryResult> ExecuteAsync(
            KustoQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<KustoQueryResult> completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Completions.Add(completion);
            return completion.Task;
        }
    }
}
