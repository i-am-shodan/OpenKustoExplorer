using Avalonia.Input;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Desktop;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Workbench;

/// <summary>
/// Verifies durable dashboard editing workflows.
/// </summary>
public sealed class KustoDashboardWorkspaceViewModelTests
{
    /// <summary>
    /// Verifies a persisted dashboard is selected after all dependent commands are initialized.
    /// </summary>
    [Fact]
    public void ConstructorSelectsPersistedDashboard()
    {
        KustoDashboard dashboard = new(Guid.NewGuid(), "Operations", "#EEF2F3", []);
        StubKustoDashboardStore store = new(new KustoDashboardCatalog([dashboard]));

        using KustoDashboardWorkspaceViewModel viewModel = new(store, new StubKustoQueryService());

        Assert.Equal("Operations", viewModel.SelectedDashboard?.Title);
        Assert.Equal("Last 24 hours", viewModel.SelectedTimeRangeOption.Label);
    }

    /// <summary>
    /// Verifies one dashboard refresh resolves identical bounds for every widget.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshSelectedSharesResolvedBoundsAcrossWidgets()
    {
        KustoDashboardWidget firstWidget = CreateWidget("First", "FirstTable | count");
        KustoDashboardWidget secondWidget = CreateWidget("Second", "SecondTable | count");
        KustoDashboard dashboard = new(
            Guid.NewGuid(),
            "Operations",
            "#EEF2F3",
            [firstWidget, secondWidget],
            KustoDashboardTimeRange.CreateRelative(TimeSpan.FromHours(6)));
        StubKustoQueryService queryService = new();
        using KustoDashboardWorkspaceViewModel viewModel = new(
            new StubKustoDashboardStore(new KustoDashboardCatalog([dashboard])),
            queryService);
        DateTimeOffset utcNow = new(2026, 9, 16, 18, 0, 0, TimeSpan.Zero);

        await viewModel.RefreshSelectedAsync(utcNow);

        Assert.Equal(2, queryService.Requests.Count);
        const string Prefix = "let _startTime = datetime(2026-09-16T12:00:00.0000000Z);\n"
            + "let _endTime = datetime(2026-09-16T18:00:00.0000000Z);\n";
        Assert.All(
            queryService.Requests,
            request => Assert.StartsWith(Prefix, request.QueryText, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies changing a preset persists it, invalidates cache, and refreshes immediately.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectingPresetPersistsInvalidatesAndRefreshes()
    {
        DateTimeOffset cachedAtUtc = DateTimeOffset.UtcNow;
        KustoQueryResult cachedResult = new([], TimeSpan.Zero);
        KustoDashboardWidget widget = CreateWidget(
            "Cached",
            "Events | count",
            cachedResult,
            cachedAtUtc);
        KustoDashboard dashboard = new(Guid.NewGuid(), "Operations", "#EEF2F3", [widget]);
        StubKustoDashboardStore store = new(new KustoDashboardCatalog([dashboard]));
        StubKustoQueryService queryService = new();
        using KustoDashboardWorkspaceViewModel viewModel = new(store, queryService);

        viewModel.SelectedTimeRangeOption = Assert.Single(
            viewModel.TimeRangeOptions,
            item => item.Duration == TimeSpan.FromHours(6));
        await viewModel.ApplyTimeRangeSelectionCommand.ExecutionTask!;

        KustoDashboard firstSave = Assert.Single(store.SavedCatalogs[0].Dashboards);
        Assert.Null(Assert.Single(firstSave.Widgets).CachedResult);
        KustoDashboard saved = Assert.Single(store.SavedCatalog!.Dashboards);
        Assert.Equal(TimeSpan.FromHours(6), saved.TimeRange.RelativeDuration);
        Assert.Single(queryService.Requests);
        Assert.Contains("let _startTime", queryService.Requests[0].QueryText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies custom local values are converted and persisted as a fixed UTC range.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CustomRangePersistsUtcBoundsAndRefreshes()
    {
        StubKustoDashboardStore store = new();
        StubKustoQueryService queryService = new();
        using KustoDashboardWorkspaceViewModel viewModel = new(
            store,
            queryService,
            localTimeZone: TimeZoneInfo.Utc);
        viewModel.AddDashboard("Operations");
        viewModel.AddWidget(CreateWidget("Events", "Events | count"));

        viewModel.SelectedTimeRangeOption = viewModel.TimeRangeOptions[^1];
        await viewModel.ApplyTimeRangeSelectionCommand.ExecutionTask!;
        viewModel.CustomTimeRangeStartDate = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        viewModel.CustomTimeRangeStartTime = TimeSpan.FromHours(8);
        viewModel.CustomTimeRangeEndDate = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        viewModel.CustomTimeRangeEndTime = TimeSpan.FromHours(9);

        await viewModel.SaveCustomTimeRangeCommand.ExecuteAsync(null);

        KustoDashboard saved = Assert.Single(store.SavedCatalog!.Dashboards);
        Assert.Equal(KustoDashboardTimeRangeKind.Absolute, saved.TimeRange.Kind);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
            saved.TimeRange.StartUtc);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero),
            saved.TimeRange.EndUtc);
        Assert.False(viewModel.IsCustomTimeRangeOpen);
        Assert.Single(queryService.Requests);
    }

    /// <summary>
    /// Verifies a pinned widget and its snapped layout are persisted through the workspace.
    /// </summary>
    [Fact]
    public void SavePinnedWidgetAndLayoutPersistsCatalog()
    {
        StubKustoDashboardStore store = new();
        using KustoDashboardWorkspaceViewModel viewModel = new(store, new StubKustoQueryService());
        viewModel.AddDashboard("Operations");
        viewModel.OpenNewWidget(
            "Errors",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Errors | count",
            null);
        viewModel.SelectedRefreshOption = viewModel.RefreshOptions[0];
        viewModel.ApplyWidgetThemeCommand.Execute(viewModel.Themes[2]);

        viewModel.SaveWidgetCommand.Execute(null);

        KustoDashboardWidgetViewModel widget = Assert.Single(viewModel.SelectedDashboard!.Widgets);
        KustoDashboard persistedDashboard = Assert.Single(store.SavedCatalog!.Dashboards);
        KustoDashboardWidget persistedWidget = Assert.Single(persistedDashboard.Widgets);
        Assert.Equal(TimeSpan.FromSeconds(30), persistedWidget.RefreshInterval);
        Assert.Equal(KustoDashboardWidgetDisplayMode.Table, persistedWidget.DisplayMode);
        Assert.Equal("#F0F7F2", persistedWidget.BackgroundColor);

        widget.PreviewLayout(7, 9, 18, 12);
        widget.CommitLayout();

        persistedWidget = Assert.Single(Assert.Single(store.SavedCatalog.Dashboards).Widgets);
        Assert.Equal(7, persistedWidget.Layout.Column);
        Assert.Equal(9, persistedWidget.Layout.Row);
        Assert.Equal(18, persistedWidget.Layout.ColumnSpan);
        Assert.Equal(12, persistedWidget.Layout.RowSpan);
    }

    /// <summary>
    /// Verifies keyboard movement and resizing share the persisted snapped-layout path.
    /// </summary>
    [Fact]
    public void KeyboardWidgetLayoutActionsMoveResizeAndPersist()
    {
        StubKustoDashboardStore store = new();
        using KustoDashboardWorkspaceViewModel viewModel = new(store, new StubKustoQueryService());
        viewModel.AddDashboard("Operations");
        viewModel.OpenNewWidget(
            "Errors",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Errors | count",
            null);
        viewModel.SaveWidgetCommand.Execute(null);
        KustoDashboardWidgetViewModel widget = Assert.Single(viewModel.SelectedDashboard!.Widgets);

        Assert.True(KustoDashboardWidgetKeyboardLayout.TryAdjust(widget, Key.Right, isResize: false));
        Assert.True(KustoDashboardWidgetKeyboardLayout.TryAdjust(widget, Key.Down, isResize: true));

        KustoDashboardWidget persisted = Assert.Single(Assert.Single(store.SavedCatalog!.Dashboards).Widgets);
        Assert.Equal(1, persisted.Layout.Column);
        Assert.Equal(widget.RowSpan, persisted.Layout.RowSpan);
        Assert.Contains("column 2", widget.LayoutAutomationText, StringComparison.Ordinal);
    }

    private static KustoDashboardWidget CreateWidget(
        string title,
        string queryText,
        KustoQueryResult? cachedResult = null,
        DateTimeOffset? cachedAtUtc = null)
    {
        return new KustoDashboardWidget(
            Guid.NewGuid(),
            title,
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            queryText,
            TimeSpan.FromMinutes(5),
            KustoDashboardWidgetDisplayMode.Table,
            KustoVisualizationKind.Table,
            new KustoDashboardWidgetLayout(0, 0, 12, 8),
            "#FFFFFF",
            "#202124",
            "#1769AA",
            cachedResult,
            cachedAtUtc);
    }

    private sealed class StubKustoDashboardStore : IKustoDashboardStore
    {
        private readonly KustoDashboardCatalog catalog;

        public StubKustoDashboardStore(KustoDashboardCatalog? catalog = null)
        {
            this.catalog = catalog ?? new KustoDashboardCatalog([]);
        }

        public KustoDashboardCatalog? SavedCatalog { get; private set; }

        public List<KustoDashboardCatalog> SavedCatalogs { get; } = [];

        public KustoDashboardCatalog Load()
        {
            return catalog;
        }

        public void Save(KustoDashboardCatalog catalog)
        {
            SavedCatalog = catalog;
            SavedCatalogs.Add(catalog);
        }

        public KustoDashboard Import(Stream stream)
        {
            throw new NotSupportedException();
        }

        public void Export(Stream stream, KustoDashboard dashboard)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubKustoQueryService : IKustoQueryService
    {
        public List<KustoQueryRequest> Requests { get; } = [];

        public Task<KustoQueryResult> ExecuteAsync(
            KustoQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new KustoQueryResult([], TimeSpan.Zero));
        }
    }
}
