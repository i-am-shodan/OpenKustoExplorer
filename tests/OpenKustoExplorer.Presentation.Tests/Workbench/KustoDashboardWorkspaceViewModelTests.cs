using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Execution;
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

        persistedWidget = Assert.Single(Assert.Single(store.SavedCatalog!.Dashboards).Widgets);
        Assert.Equal(7, persistedWidget.Layout.Column);
        Assert.Equal(9, persistedWidget.Layout.Row);
        Assert.Equal(18, persistedWidget.Layout.ColumnSpan);
        Assert.Equal(12, persistedWidget.Layout.RowSpan);
    }

    private sealed class StubKustoDashboardStore : IKustoDashboardStore
    {
        private readonly KustoDashboardCatalog catalog;

        public StubKustoDashboardStore(KustoDashboardCatalog? catalog = null)
        {
            this.catalog = catalog ?? new KustoDashboardCatalog([]);
        }

        public KustoDashboardCatalog? SavedCatalog { get; private set; }

        public KustoDashboardCatalog Load()
        {
            return catalog;
        }

        public void Save(KustoDashboardCatalog catalog)
        {
            SavedCatalog = catalog;
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
        public Task<KustoQueryResult> ExecuteAsync(
            KustoQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new KustoQueryResult([], TimeSpan.Zero));
        }
    }
}
