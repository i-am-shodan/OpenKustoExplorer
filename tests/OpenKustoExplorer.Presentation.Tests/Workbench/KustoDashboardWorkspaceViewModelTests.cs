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
        viewModel.CustomTimeRangeStartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);
        viewModel.CustomTimeRangeStartTime = TimeSpan.FromHours(8);
        viewModel.CustomTimeRangeEndDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Unspecified);
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

    /// <summary>
    /// Verifies dashboard editor validation, editing, themes, and deletion share persisted state.
    /// </summary>
    [Fact]
    public void DashboardEditorCreatesEditsAndDeletesDashboard()
    {
        StubKustoDashboardStore store = new();
        using KustoDashboardWorkspaceViewModel viewModel = new(store, new StubKustoQueryService());

        viewModel.OpenCreateDashboardCommand.Execute(null);
        Assert.True(viewModel.IsDashboardEditorOpen);
        Assert.Equal("New dashboard", viewModel.DashboardEditorHeading);
        Assert.Equal("Create", viewModel.DashboardEditorActionText);
        viewModel.DashboardEditorTitle = "Operations";
        viewModel.ApplyDashboardThemeCommand.Execute(viewModel.Themes[3]);
        viewModel.SaveDashboardCommand.Execute(null);

        Assert.False(viewModel.IsDashboardEditorOpen);
        Assert.Equal("Operations", viewModel.SelectedDashboard?.Title);
        Assert.Equal("#20252B", viewModel.SelectedDashboard?.BackgroundColor);

        viewModel.OpenCreateDashboardCommand.Execute(null);
        viewModel.DashboardEditorTitle = " operations ";
        viewModel.SaveDashboardCommand.Execute(null);

        Assert.True(viewModel.IsDashboardEditorOpen);
        Assert.True(viewModel.HasDashboardEditorError);
        Assert.Equal("Choose a unique dashboard name.", viewModel.DashboardEditorErrorText);
        viewModel.CloseDashboardEditorCommand.Execute(null);
        Assert.False(viewModel.HasDashboardEditorError);

        viewModel.OpenEditDashboardCommand.Execute(null);
        Assert.Equal("Edit dashboard", viewModel.DashboardEditorHeading);
        Assert.Equal("Save", viewModel.DashboardEditorActionText);
        viewModel.DashboardEditorTitle = "Renamed";
        viewModel.ApplyDashboardThemeCommand.Execute(viewModel.Themes[4]);
        viewModel.SaveDashboardCommand.Execute(null);

        Assert.Equal("Renamed", viewModel.SelectedDashboard?.Title);
        Assert.Equal("#FFF2F5", viewModel.SelectedDashboard?.BackgroundColor);
        viewModel.OpenDeleteDashboardCommand.Execute(null);
        Assert.True(viewModel.IsDeleteDashboardOpen);
        viewModel.CloseDeleteDashboardCommand.Execute(null);
        Assert.False(viewModel.IsDeleteDashboardOpen);
        viewModel.OpenDeleteDashboardCommand.Execute(null);
        viewModel.ConfirmDeleteDashboardCommand.Execute(null);
        Assert.Empty(viewModel.Dashboards);
        Assert.True(viewModel.ShowEmptyState);
        Assert.Null(viewModel.SelectedDashboard);
        Assert.Empty(store.SavedCatalog!.Dashboards);
    }

    /// <summary>
    /// Verifies editing can move a widget between dashboards and deletion persists the destination.
    /// </summary>
    [Fact]
    public void WidgetEditorMovesThemesAndDeletesWidget()
    {
        StubKustoDashboardStore store = new();
        using KustoDashboardWorkspaceViewModel viewModel = new(store, new StubKustoQueryService());
        KustoDashboardViewModel sourceDashboard = viewModel.AddDashboard("Source");
        viewModel.OpenNewWidget(
            "Errors",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Errors | count",
            null);
        viewModel.SaveWidgetCommand.Execute(null);
        KustoDashboardWidgetViewModel sourceWidget = Assert.Single(sourceDashboard.Widgets);
        KustoDashboardViewModel destinationDashboard = viewModel.AddDashboard("Destination");

        viewModel.OpenEditWidgetCommand.Execute(sourceWidget);
        Assert.Same(sourceDashboard, viewModel.SelectedDashboard);
        Assert.Equal("Edit widget", viewModel.WidgetEditorHeading);
        Assert.Equal("Save", viewModel.WidgetEditorActionText);
        viewModel.WidgetEditorTitle = "Errors by region";
        viewModel.WidgetEditorUsesVisualization = true;
        viewModel.SelectedVisualizationOption = Assert.Single(
            viewModel.VisualizationOptions,
            item => item.Kind == KustoVisualizationKind.PieChart);
        viewModel.ApplyWidgetThemeCommand.Execute(viewModel.Themes[2]);
        viewModel.SelectedDashboard = destinationDashboard;
        viewModel.SaveWidgetCommand.Execute(null);

        Assert.Empty(sourceDashboard.Widgets);
        KustoDashboardWidgetViewModel movedWidget = Assert.Single(destinationDashboard.Widgets);
        Assert.Equal(sourceWidget.Id, movedWidget.Id);
        Assert.Equal("Errors by region", movedWidget.Title);
        Assert.Equal(KustoDashboardWidgetDisplayMode.Visualization, movedWidget.DisplayMode);
        Assert.Equal(KustoVisualizationKind.PieChart, movedWidget.VisualizationKind);
        Assert.Equal("#F0F7F2", movedWidget.BackgroundColor);

        viewModel.OpenDeleteWidgetCommand.Execute(movedWidget);
        Assert.True(viewModel.IsDeleteWidgetOpen);
        viewModel.CloseDeleteWidgetCommand.Execute(null);
        Assert.False(viewModel.IsDeleteWidgetOpen);
        viewModel.OpenDeleteWidgetCommand.Execute(movedWidget);
        viewModel.ConfirmDeleteWidgetCommand.Execute(null);
        Assert.Empty(destinationDashboard.Widgets);
        Assert.Empty(Assert.Single(store.SavedCatalog!.Dashboards, item => item.Title == "Destination").Widgets);
    }

    /// <summary>
    /// Verifies invalid custom bounds disable saving and closing restores the persisted preset.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CustomRangeValidationRejectsReversedBoundsAndCanClose()
    {
        using KustoDashboardWorkspaceViewModel viewModel = new(
            new StubKustoDashboardStore(),
            new StubKustoQueryService(),
            localTimeZone: TimeZoneInfo.Utc);
        viewModel.AddDashboard("Operations");
        viewModel.SelectedTimeRangeOption = viewModel.TimeRangeOptions[^1];
        await viewModel.ApplyTimeRangeSelectionCommand.ExecutionTask!;

        viewModel.CustomTimeRangeStartDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Unspecified);
        viewModel.CustomTimeRangeStartTime = TimeSpan.FromHours(10);
        viewModel.CustomTimeRangeEndDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);
        viewModel.CustomTimeRangeEndTime = TimeSpan.FromHours(9);

        Assert.True(viewModel.HasCustomTimeRangeError);
        Assert.False(viewModel.CanSaveCustomTimeRange);
        Assert.False(viewModel.SaveCustomTimeRangeCommand.CanExecute(null));

        viewModel.CustomTimeRangeEndDate = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Unspecified);
        Assert.False(viewModel.HasCustomTimeRangeError);
        Assert.True(viewModel.CanSaveCustomTimeRange);
        viewModel.CloseCustomTimeRangeCommand.Execute(null);
        Assert.False(viewModel.IsCustomTimeRangeOpen);
        Assert.Equal("Last 24 hours", viewModel.SelectedTimeRangeOption.Label);
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

        public Task SaveAsync(
            KustoDashboardCatalog catalog,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedCatalog = catalog;
            SavedCatalogs.Add(catalog);
            return Task.CompletedTask;
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
