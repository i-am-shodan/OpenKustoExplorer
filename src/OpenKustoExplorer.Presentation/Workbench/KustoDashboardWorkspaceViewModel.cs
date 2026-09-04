using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Coordinates persisted dashboards and refreshes the selected dashboard.
/// </summary>
public sealed class KustoDashboardWorkspaceViewModel : ObservableObject, IDisposable
{
    private const string DefaultDashboardBackground = "#EEF2F3";
    private const int DashboardColumnCount = 48;
    private const int DefaultWidgetColumnSpan = 16;
    private const int DefaultWidgetRowSpan = 11;
    private readonly IKustoDashboardStore dashboardStore;
    private readonly IKustoQueryService queryService;
    private KustoDashboardViewModel? dashboardBeingEdited;
    private string dashboardEditorBackgroundColor = DefaultDashboardBackground;
    private string dashboardEditorErrorText = string.Empty;
    private string dashboardEditorTitle = string.Empty;
    private bool isDisposed;
    private bool isDashboardEditorOpen;
    private bool isDeleteDashboardOpen;
    private bool isDeleteWidgetOpen;
    private bool isRefreshing;
    private bool isWidgetEditorOpen;
    private KustoDashboardViewModel? dashboardContainingEditedWidget;
    private KustoDashboardViewModel? selectedDashboard;
    private KustoDashboardRefreshOptionViewModel selectedRefreshOption;
    private KustoDashboardVisualizationOptionViewModel selectedVisualizationOption;
    private KustoDashboardWidgetViewModel? widgetBeingEdited;
    private KustoDashboardWidgetViewModel? widgetPendingDeletion;
    private string widgetEditorAccentColor = string.Empty;
    private string widgetEditorBackgroundColor = string.Empty;
    private string widgetEditorClusterAddress = string.Empty;
    private string widgetEditorDatabaseName = string.Empty;
    private string widgetEditorErrorText = string.Empty;
    private string widgetEditorForegroundColor = string.Empty;
    private string widgetEditorQueryText = string.Empty;
    private string widgetEditorTitle = string.Empty;
    private KustoDashboardWidgetDisplayMode widgetEditorDisplayMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardWorkspaceViewModel"/> class.
    /// </summary>
    /// <param name="dashboardStore">The durable dashboard store.</param>
    /// <param name="queryService">The Kusto query service.</param>
    public KustoDashboardWorkspaceViewModel(
        IKustoDashboardStore dashboardStore,
        IKustoQueryService queryService)
    {
        ArgumentNullException.ThrowIfNull(dashboardStore);
        ArgumentNullException.ThrowIfNull(queryService);
        this.dashboardStore = dashboardStore;
        this.queryService = queryService;
        RefreshOptions = CreateRefreshOptions();
        VisualizationOptions = CreateVisualizationOptions();
        Themes = CreateThemes();
        selectedRefreshOption = RefreshOptions[2];
        selectedVisualizationOption = VisualizationOptions[0];
        Dashboards = new ObservableCollection<KustoDashboardViewModel>(
            dashboardStore.Load().Dashboards.Select(CreateDashboardViewModel));
        RefreshCommand = new AsyncRelayCommand(RefreshSelectedFromCommandAsync);
        OpenCreateDashboardCommand = new RelayCommand(OpenCreateDashboard);
        OpenEditDashboardCommand = new RelayCommand(OpenEditDashboard, () => SelectedDashboard is not null);
        CloseDashboardEditorCommand = new RelayCommand(CloseDashboardEditor);
        SaveDashboardCommand = new RelayCommand(SaveDashboard, () => CanSaveDashboard);
        OpenDeleteDashboardCommand = new RelayCommand(
            () => IsDeleteDashboardOpen = SelectedDashboard is not null,
            () => SelectedDashboard is not null);
        CloseDeleteDashboardCommand = new RelayCommand(() => IsDeleteDashboardOpen = false);
        ConfirmDeleteDashboardCommand = new RelayCommand(DeleteSelectedDashboard);
        OpenEditWidgetCommand = new RelayCommand<KustoDashboardWidgetViewModel>(OpenEditWidget);
        CloseWidgetEditorCommand = new RelayCommand(CloseWidgetEditor);
        SaveWidgetCommand = new RelayCommand(SaveWidget, () => CanSaveWidget);
        OpenDeleteWidgetCommand = new RelayCommand<KustoDashboardWidgetViewModel>(OpenDeleteWidget);
        CloseDeleteWidgetCommand = new RelayCommand(CloseDeleteWidget);
        ConfirmDeleteWidgetCommand = new RelayCommand(ConfirmDeleteWidget);
        ApplyDashboardThemeCommand = new RelayCommand<KustoDashboardThemeViewModel>(ApplyDashboardTheme);
        ApplyWidgetThemeCommand = new RelayCommand<KustoDashboardThemeViewModel>(ApplyWidgetTheme);
        SelectedDashboard = Dashboards.FirstOrDefault();
    }

    /// <summary>
    /// Gets persisted dashboards in display order.
    /// </summary>
    public ObservableCollection<KustoDashboardViewModel> Dashboards { get; }

    /// <summary>
    /// Gets or sets the active dashboard.
    /// </summary>
    public KustoDashboardViewModel? SelectedDashboard
    {
        get => selectedDashboard;
        set
        {
            if (SetProperty(ref selectedDashboard, value))
            {
                OnPropertyChanged(nameof(HasSelectedDashboard));
                OnPropertyChanged(nameof(ShowEmptyState));
                OpenEditDashboardCommand.NotifyCanExecuteChanged();
                OpenDeleteDashboardCommand.NotifyCanExecuteChanged();
                SaveWidgetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a dashboard is selected.
    /// </summary>
    public bool HasSelectedDashboard => SelectedDashboard is not null;

    /// <summary>
    /// Gets a value indicating whether no dashboards exist.
    /// </summary>
    public bool ShowEmptyState => Dashboards.Count == 0;

    /// <summary>
    /// Gets a value indicating whether selected widgets are refreshing.
    /// </summary>
    public bool IsRefreshing
    {
        get => isRefreshing;
        private set => SetProperty(ref isRefreshing, value);
    }

    /// <summary>
    /// Gets the command that refreshes every widget on the selected dashboard.
    /// </summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Gets available automatic refresh intervals.
    /// </summary>
    public IReadOnlyList<KustoDashboardRefreshOptionViewModel> RefreshOptions { get; }

    /// <summary>
    /// Gets available dashboard visualization kinds.
    /// </summary>
    public IReadOnlyList<KustoDashboardVisualizationOptionViewModel> VisualizationOptions { get; }

    /// <summary>
    /// Gets coordinated dashboard and widget color themes.
    /// </summary>
    public IReadOnlyList<KustoDashboardThemeViewModel> Themes { get; }

    /// <summary>
    /// Gets the command that opens creation of a dashboard.
    /// </summary>
    public IRelayCommand OpenCreateDashboardCommand { get; }

    /// <summary>
    /// Gets the command that opens editing of the selected dashboard.
    /// </summary>
    public IRelayCommand OpenEditDashboardCommand { get; }

    /// <summary>
    /// Gets the command that closes the dashboard editor.
    /// </summary>
    public IRelayCommand CloseDashboardEditorCommand { get; }

    /// <summary>
    /// Gets the command that saves dashboard details.
    /// </summary>
    public IRelayCommand SaveDashboardCommand { get; }

    /// <summary>
    /// Gets the command that opens dashboard deletion confirmation.
    /// </summary>
    public IRelayCommand OpenDeleteDashboardCommand { get; }

    /// <summary>
    /// Gets the command that closes dashboard deletion confirmation.
    /// </summary>
    public IRelayCommand CloseDeleteDashboardCommand { get; }

    /// <summary>
    /// Gets the command that confirms dashboard deletion.
    /// </summary>
    public IRelayCommand ConfirmDeleteDashboardCommand { get; }

    /// <summary>
    /// Gets the command that opens an existing widget for editing.
    /// </summary>
    public IRelayCommand<KustoDashboardWidgetViewModel> OpenEditWidgetCommand { get; }

    /// <summary>
    /// Gets the command that closes the widget editor.
    /// </summary>
    public IRelayCommand CloseWidgetEditorCommand { get; }

    /// <summary>
    /// Gets the command that saves the widget editor.
    /// </summary>
    public IRelayCommand SaveWidgetCommand { get; }

    /// <summary>
    /// Gets the command that opens widget deletion confirmation.
    /// </summary>
    public IRelayCommand<KustoDashboardWidgetViewModel> OpenDeleteWidgetCommand { get; }

    /// <summary>
    /// Gets the command that closes widget deletion confirmation.
    /// </summary>
    public IRelayCommand CloseDeleteWidgetCommand { get; }

    /// <summary>
    /// Gets the command that confirms widget deletion.
    /// </summary>
    public IRelayCommand ConfirmDeleteWidgetCommand { get; }

    /// <summary>
    /// Gets the command that applies a color theme to the dashboard editor.
    /// </summary>
    public IRelayCommand<KustoDashboardThemeViewModel> ApplyDashboardThemeCommand { get; }

    /// <summary>
    /// Gets the command that applies a color theme to the widget editor.
    /// </summary>
    public IRelayCommand<KustoDashboardThemeViewModel> ApplyWidgetThemeCommand { get; }

    /// <summary>
    /// Gets a value indicating whether the dashboard editor is open.
    /// </summary>
    public bool IsDashboardEditorOpen
    {
        get => isDashboardEditorOpen;
        private set
        {
            if (SetProperty(ref isDashboardEditorOpen, value))
            {
                SaveDashboardCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether dashboard deletion confirmation is open.
    /// </summary>
    public bool IsDeleteDashboardOpen
    {
        get => isDeleteDashboardOpen;
        private set => SetProperty(ref isDeleteDashboardOpen, value);
    }

    /// <summary>
    /// Gets or sets the dashboard title being edited.
    /// </summary>
    public string DashboardEditorTitle
    {
        get => dashboardEditorTitle;
        set
        {
            if (SetProperty(ref dashboardEditorTitle, value ?? string.Empty))
            {
                SaveDashboardCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets the dashboard editor heading.
    /// </summary>
    public string DashboardEditorHeading => dashboardBeingEdited is null ? "New dashboard" : "Edit dashboard";

    /// <summary>
    /// Gets the dashboard editor action text.
    /// </summary>
    public string DashboardEditorActionText => dashboardBeingEdited is null ? "Create" : "Save";

    /// <summary>
    /// Gets the dashboard canvas color being edited.
    /// </summary>
    public string DashboardEditorBackgroundColor
    {
        get => dashboardEditorBackgroundColor;
        private set => SetProperty(ref dashboardEditorBackgroundColor, value);
    }

    /// <summary>
    /// Gets dashboard editor validation feedback.
    /// </summary>
    public string DashboardEditorErrorText
    {
        get => dashboardEditorErrorText;
        private set
        {
            if (SetProperty(ref dashboardEditorErrorText, value))
            {
                OnPropertyChanged(nameof(HasDashboardEditorError));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether dashboard editor validation failed.
    /// </summary>
    public bool HasDashboardEditorError => DashboardEditorErrorText.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the widget editor is open.
    /// </summary>
    public bool IsWidgetEditorOpen
    {
        get => isWidgetEditorOpen;
        private set
        {
            if (SetProperty(ref isWidgetEditorOpen, value))
            {
                SaveWidgetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether widget deletion confirmation is open.
    /// </summary>
    public bool IsDeleteWidgetOpen
    {
        get => isDeleteWidgetOpen;
        private set => SetProperty(ref isDeleteWidgetOpen, value);
    }

    /// <summary>
    /// Gets the widget editor heading.
    /// </summary>
    public string WidgetEditorHeading => widgetBeingEdited is null ? "Pin query to dashboard" : "Edit widget";

    /// <summary>
    /// Gets the widget editor action text.
    /// </summary>
    public string WidgetEditorActionText => widgetBeingEdited is null ? "Add widget" : "Save";

    /// <summary>
    /// Gets or sets the widget title being edited.
    /// </summary>
    public string WidgetEditorTitle
    {
        get => widgetEditorTitle;
        set
        {
            if (SetProperty(ref widgetEditorTitle, value ?? string.Empty))
            {
                SaveWidgetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the widget cluster address being edited.
    /// </summary>
    public string WidgetEditorClusterAddress
    {
        get => widgetEditorClusterAddress;
        set
        {
            if (SetProperty(ref widgetEditorClusterAddress, value ?? string.Empty))
            {
                SaveWidgetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the widget database being edited.
    /// </summary>
    public string WidgetEditorDatabaseName
    {
        get => widgetEditorDatabaseName;
        set
        {
            if (SetProperty(ref widgetEditorDatabaseName, value ?? string.Empty))
            {
                SaveWidgetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the widget query being edited.
    /// </summary>
    public string WidgetEditorQueryText
    {
        get => widgetEditorQueryText;
        set
        {
            if (SetProperty(ref widgetEditorQueryText, value ?? string.Empty))
            {
                SaveWidgetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected refresh interval.
    /// </summary>
    public KustoDashboardRefreshOptionViewModel SelectedRefreshOption
    {
        get => selectedRefreshOption;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetProperty(ref selectedRefreshOption, value);
        }
    }

    /// <summary>
    /// Gets or sets how widget results are displayed.
    /// </summary>
    public KustoDashboardWidgetDisplayMode WidgetEditorDisplayMode
    {
        get => widgetEditorDisplayMode;
        set
        {
            if (SetProperty(ref widgetEditorDisplayMode, value))
            {
                OnPropertyChanged(nameof(WidgetEditorUsesTable));
                OnPropertyChanged(nameof(WidgetEditorUsesVisualization));
                SaveWidgetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the widget uses a table.
    /// </summary>
    public bool WidgetEditorUsesTable
    {
        get => WidgetEditorDisplayMode == KustoDashboardWidgetDisplayMode.Table;
        set
        {
            if (value)
            {
                WidgetEditorDisplayMode = KustoDashboardWidgetDisplayMode.Table;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the widget uses a visualization.
    /// </summary>
    public bool WidgetEditorUsesVisualization
    {
        get => WidgetEditorDisplayMode == KustoDashboardWidgetDisplayMode.Visualization;
        set
        {
            if (value)
            {
                WidgetEditorDisplayMode = KustoDashboardWidgetDisplayMode.Visualization;
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected visualization kind.
    /// </summary>
    public KustoDashboardVisualizationOptionViewModel SelectedVisualizationOption
    {
        get => selectedVisualizationOption;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetProperty(ref selectedVisualizationOption, value);
        }
    }

    /// <summary>
    /// Gets the widget surface color being edited.
    /// </summary>
    public string WidgetEditorBackgroundColor
    {
        get => widgetEditorBackgroundColor;
        private set => SetProperty(ref widgetEditorBackgroundColor, value);
    }

    /// <summary>
    /// Gets the widget text color being edited.
    /// </summary>
    public string WidgetEditorForegroundColor
    {
        get => widgetEditorForegroundColor;
        private set => SetProperty(ref widgetEditorForegroundColor, value);
    }

    /// <summary>
    /// Gets the widget accent color being edited.
    /// </summary>
    public string WidgetEditorAccentColor
    {
        get => widgetEditorAccentColor;
        private set => SetProperty(ref widgetEditorAccentColor, value);
    }

    /// <summary>
    /// Gets widget editor validation feedback.
    /// </summary>
    public string WidgetEditorErrorText
    {
        get => widgetEditorErrorText;
        private set
        {
            if (SetProperty(ref widgetEditorErrorText, value))
            {
                OnPropertyChanged(nameof(HasWidgetEditorError));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether widget editor validation failed.
    /// </summary>
    public bool HasWidgetEditorError => WidgetEditorErrorText.Length > 0;

    /// <summary>
    /// Gets a value indicating whether dashboard details can be saved.
    /// </summary>
    public bool CanSaveDashboard => IsDashboardEditorOpen && !string.IsNullOrWhiteSpace(DashboardEditorTitle);

    /// <summary>
    /// Gets a value indicating whether widget details can be saved.
    /// </summary>
    public bool CanSaveWidget => IsWidgetEditorOpen
        && SelectedDashboard is not null
        && !string.IsNullOrWhiteSpace(WidgetEditorTitle)
        && Uri.TryCreate(WidgetEditorClusterAddress, UriKind.Absolute, out Uri? clusterUri)
        && clusterUri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(WidgetEditorDatabaseName)
        && !string.IsNullOrWhiteSpace(WidgetEditorQueryText);

    /// <summary>
    /// Creates and selects an empty dashboard.
    /// </summary>
    /// <param name="title">The dashboard title.</param>
    /// <returns>The new dashboard.</returns>
    public KustoDashboardViewModel AddDashboard(string title)
    {
        KustoDashboard definition = new(Guid.NewGuid(), title, DefaultDashboardBackground, []);
        KustoDashboardViewModel dashboard = CreateDashboardViewModel(definition);
        Dashboards.Add(dashboard);
        SelectedDashboard = dashboard;
        OnPropertyChanged(nameof(ShowEmptyState));
        Save();
        return dashboard;
    }

    /// <summary>
    /// Opens creation of a widget using the current query context.
    /// </summary>
    /// <param name="title">The suggested widget title.</param>
    /// <param name="clusterUri">The query cluster URI.</param>
    /// <param name="databaseName">The query database.</param>
    /// <param name="queryText">The selected KQL query.</param>
    /// <param name="visualizationKind">An optional current visualization.</param>
    public void OpenNewWidget(
        string title,
        Uri clusterUri,
        string databaseName,
        string queryText,
        KustoVisualizationKind? visualizationKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        widgetBeingEdited = null;
        dashboardContainingEditedWidget = null;
        WidgetEditorTitle = title;
        WidgetEditorClusterAddress = clusterUri.AbsoluteUri;
        WidgetEditorDatabaseName = databaseName;
        WidgetEditorQueryText = queryText;
        SelectedRefreshOption = RefreshOptions[2];
        WidgetEditorDisplayMode = visualizationKind is null
            ? KustoDashboardWidgetDisplayMode.Table
            : KustoDashboardWidgetDisplayMode.Visualization;
        SelectedVisualizationOption = VisualizationOptions.FirstOrDefault(
            item => item.Kind == visualizationKind)
            ?? VisualizationOptions[0];
        ApplyWidgetTheme(Themes[0]);
        WidgetEditorErrorText = string.Empty;
        OnPropertyChanged(nameof(WidgetEditorHeading));
        OnPropertyChanged(nameof(WidgetEditorActionText));
        IsWidgetEditorOpen = true;
    }

    /// <summary>
    /// Deletes the selected dashboard.
    /// </summary>
    public void DeleteSelectedDashboard()
    {
        KustoDashboardViewModel? dashboard = SelectedDashboard;

        if (dashboard is not null && Dashboards.Remove(dashboard))
        {
            dashboard.Dispose();
            SelectedDashboard = Dashboards.FirstOrDefault();
            OnPropertyChanged(nameof(ShowEmptyState));
            IsDeleteDashboardOpen = false;
            Save();
        }
    }

    /// <summary>
    /// Adds a query-backed widget to the selected dashboard.
    /// </summary>
    /// <param name="definition">The widget definition.</param>
    /// <returns>The new runtime widget.</returns>
    public KustoDashboardWidgetViewModel AddWidget(KustoDashboardWidget definition)
    {
        KustoDashboardViewModel dashboard = SelectedDashboard
            ?? throw new InvalidOperationException("Select a dashboard before adding a widget.");
        return dashboard.AddWidget(definition);
    }

    /// <summary>
    /// Refreshes every widget on the selected dashboard.
    /// </summary>
    /// <param name="utcNow">The UTC time used to schedule subsequent refreshes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing all refreshes.</returns>
    public async Task RefreshSelectedAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        if (IsRefreshing || SelectedDashboard is null)
        {
            return;
        }

        IsRefreshing = true;

        try
        {
            await Task.WhenAll(SelectedDashboard.Widgets
                .Select(item => item.RefreshAsync(utcNow, cancellationToken)));
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Refreshes selected widgets whose intervals have elapsed.
    /// </summary>
    /// <param name="utcNow">The current UTC time.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing due refreshes.</returns>
    public async Task RefreshDueWidgetsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        if (IsRefreshing || SelectedDashboard is null)
        {
            return;
        }

        IsRefreshing = true;

        try
        {
            await Task.WhenAll(SelectedDashboard.Widgets
                .Select(item => item.RefreshIfDueAsync(utcNow, cancellationToken)));
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Imports and selects one dashboard, regenerating identifiers on collision.
    /// </summary>
    /// <param name="stream">The readable dashboard JSON stream.</param>
    /// <returns>The imported dashboard.</returns>
    public KustoDashboardViewModel Import(Stream stream)
    {
        KustoDashboard definition = dashboardStore.Import(stream);

        if (Dashboards.Any(item => item.Id == definition.Id))
        {
            definition = CloneWithNewIdentifiers(definition);
        }

        KustoDashboardViewModel dashboard = CreateDashboardViewModel(definition);
        Dashboards.Add(dashboard);
        SelectedDashboard = dashboard;
        OnPropertyChanged(nameof(ShowEmptyState));
        Save();
        return dashboard;
    }

    /// <summary>
    /// Exports the selected dashboard.
    /// </summary>
    /// <param name="stream">The writable JSON stream.</param>
    public void ExportSelected(Stream stream)
    {
        KustoDashboardViewModel dashboard = SelectedDashboard
            ?? throw new InvalidOperationException("Select a dashboard before exporting.");
        dashboardStore.Export(stream, dashboard.CreateDefinition());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            RefreshCommand.Cancel();

            foreach (KustoDashboardViewModel dashboard in Dashboards)
            {
                dashboard.Dispose();
            }
        }
    }

    private static KustoDashboard CloneWithNewIdentifiers(KustoDashboard source)
    {
        return new KustoDashboard(
            Guid.NewGuid(),
            source.Title,
            source.BackgroundColor,
            source.Widgets.Select(item => new KustoDashboardWidget(
                Guid.NewGuid(),
                item.Title,
                item.ClusterUri,
                item.DatabaseName,
                item.QueryText,
                item.RefreshInterval,
                item.DisplayMode,
                item.VisualizationKind,
                item.Layout,
                item.BackgroundColor,
                item.ForegroundColor,
                item.AccentColor)));
    }

    private static ReadOnlyCollection<KustoDashboardRefreshOptionViewModel> CreateRefreshOptions()
    {
        return Array.AsReadOnly(new KustoDashboardRefreshOptionViewModel[]
        {
            new("30 seconds", TimeSpan.FromSeconds(30)),
            new("1 minute", TimeSpan.FromMinutes(1)),
            new("5 minutes", TimeSpan.FromMinutes(5)),
            new("15 minutes", TimeSpan.FromMinutes(15)),
            new("30 minutes", TimeSpan.FromMinutes(30)),
            new("1 hour", TimeSpan.FromHours(1)),
        });
    }

    private static ReadOnlyCollection<KustoDashboardVisualizationOptionViewModel> CreateVisualizationOptions()
    {
        return Array.AsReadOnly(new KustoDashboardVisualizationOptionViewModel[]
        {
            new("Time chart", KustoVisualizationKind.TimeChart),
            new("Line chart", KustoVisualizationKind.LineChart),
            new("Column chart", KustoVisualizationKind.ColumnChart),
            new("Bar chart", KustoVisualizationKind.BarChart),
            new("Area chart", KustoVisualizationKind.AreaChart),
            new("Stacked area", KustoVisualizationKind.StackedAreaChart),
            new("Scatter chart", KustoVisualizationKind.ScatterChart),
            new("Pie chart", KustoVisualizationKind.PieChart),
            new("Treemap", KustoVisualizationKind.TreeMap),
            new("Card", KustoVisualizationKind.Card),
            new("Anomaly chart", KustoVisualizationKind.AnomalyChart),
            new("Ladder chart", KustoVisualizationKind.LadderChart),
            new("Pivot chart", KustoVisualizationKind.PivotChart),
            new("Time pivot", KustoVisualizationKind.TimePivot),
        });
    }

    private static ReadOnlyCollection<KustoDashboardThemeViewModel> CreateThemes()
    {
        return Array.AsReadOnly(new KustoDashboardThemeViewModel[]
        {
            new("Cloud", "#FFFFFF", "#1F2933", "#167D8D"),
            new("Signal", "#FFF8E7", "#27231D", "#C2410C"),
            new("Field", "#F0F7F2", "#173B2A", "#27864A"),
            new("Ink", "#20252B", "#F4F7F8", "#4CC9C0"),
            new("Rose", "#FFF2F5", "#3E222A", "#C43D67"),
        });
    }

    private void ApplyDashboardTheme(KustoDashboardThemeViewModel? theme)
    {
        if (theme is not null)
        {
            DashboardEditorBackgroundColor = theme.BackgroundColor;
        }
    }

    private void ApplyWidgetTheme(KustoDashboardThemeViewModel? theme)
    {
        if (theme is not null)
        {
            WidgetEditorBackgroundColor = theme.BackgroundColor;
            WidgetEditorForegroundColor = theme.ForegroundColor;
            WidgetEditorAccentColor = theme.AccentColor;
        }
    }

    private void CloseDashboardEditor()
    {
        IsDashboardEditorOpen = false;
        DashboardEditorErrorText = string.Empty;
        dashboardBeingEdited = null;
    }

    private void CloseDeleteWidget()
    {
        IsDeleteWidgetOpen = false;
        widgetPendingDeletion = null;
    }

    private void CloseWidgetEditor()
    {
        IsWidgetEditorOpen = false;
        WidgetEditorErrorText = string.Empty;
        widgetBeingEdited = null;
        dashboardContainingEditedWidget = null;
    }

    private void ConfirmDeleteWidget()
    {
        KustoDashboardWidgetViewModel? widget = widgetPendingDeletion;

        KustoDashboardViewModel? owningDashboard = widget is null
            ? null
            : Dashboards.FirstOrDefault(item => item.Widgets.Contains(widget));
        if (widget is not null && owningDashboard is not null)
        {
            owningDashboard.RemoveWidget(widget);
        }

        CloseDeleteWidget();
    }

    private KustoDashboardWidgetLayout CreateNextWidgetLayout()
    {
        int widgetIndex = SelectedDashboard?.Widgets.Count ?? 0;
        int widgetsPerRow = DashboardColumnCount / DefaultWidgetColumnSpan;
        return new KustoDashboardWidgetLayout(
            (widgetIndex % widgetsPerRow) * DefaultWidgetColumnSpan,
            (widgetIndex / widgetsPerRow) * DefaultWidgetRowSpan,
            DefaultWidgetColumnSpan,
            DefaultWidgetRowSpan);
    }

    private void OpenCreateDashboard()
    {
        dashboardBeingEdited = null;
        DashboardEditorTitle = $"Dashboard {Dashboards.Count + 1:N0}";
        DashboardEditorBackgroundColor = DefaultDashboardBackground;
        DashboardEditorErrorText = string.Empty;
        OnPropertyChanged(nameof(DashboardEditorHeading));
        OnPropertyChanged(nameof(DashboardEditorActionText));
        IsDashboardEditorOpen = true;
    }

    private void OpenDeleteWidget(KustoDashboardWidgetViewModel? widget)
    {
        widgetPendingDeletion = widget;
        IsDeleteWidgetOpen = widget is not null;
    }

    private void OpenEditDashboard()
    {
        if (SelectedDashboard is KustoDashboardViewModel dashboard)
        {
            dashboardBeingEdited = dashboard;
            DashboardEditorTitle = dashboard.Title;
            DashboardEditorBackgroundColor = dashboard.BackgroundColor;
            DashboardEditorErrorText = string.Empty;
            OnPropertyChanged(nameof(DashboardEditorHeading));
            OnPropertyChanged(nameof(DashboardEditorActionText));
            IsDashboardEditorOpen = true;
        }
    }

    private void OpenEditWidget(KustoDashboardWidgetViewModel? widget)
    {
        if (widget is not null)
        {
            widgetBeingEdited = widget;
            dashboardContainingEditedWidget = Dashboards.FirstOrDefault(
                item => item.Widgets.Contains(widget));
            SelectedDashboard = dashboardContainingEditedWidget ?? SelectedDashboard;
            WidgetEditorTitle = widget.Title;
            WidgetEditorClusterAddress = widget.ClusterUri.AbsoluteUri;
            WidgetEditorDatabaseName = widget.DatabaseName;
            WidgetEditorQueryText = widget.QueryText;
            SelectedRefreshOption = RefreshOptions.FirstOrDefault(
                item => item.Interval == widget.RefreshInterval)
                ?? new KustoDashboardRefreshOptionViewModel(
                    widget.RefreshIntervalText,
                    widget.RefreshInterval);
            WidgetEditorDisplayMode = widget.DisplayMode;
            SelectedVisualizationOption = VisualizationOptions.FirstOrDefault(
                item => item.Kind == widget.VisualizationKind)
                ?? VisualizationOptions[0];
            WidgetEditorBackgroundColor = widget.BackgroundColor;
            WidgetEditorForegroundColor = widget.ForegroundColor;
            WidgetEditorAccentColor = widget.AccentColor;
            WidgetEditorErrorText = string.Empty;
            OnPropertyChanged(nameof(WidgetEditorHeading));
            OnPropertyChanged(nameof(WidgetEditorActionText));
            IsWidgetEditorOpen = true;
        }
    }

    private void SaveDashboard()
    {
        DashboardEditorErrorText = string.Empty;

        try
        {
            bool duplicateTitle = Dashboards.Any(item => !ReferenceEquals(item, dashboardBeingEdited)
                && string.Equals(item.Title, DashboardEditorTitle.Trim(), StringComparison.OrdinalIgnoreCase));
            if (duplicateTitle)
            {
                throw new InvalidOperationException("Choose a unique dashboard name.");
            }

            if (dashboardBeingEdited is null)
            {
                KustoDashboardViewModel dashboard = AddDashboard(DashboardEditorTitle);
                dashboard.ApplyDetails(DashboardEditorTitle, DashboardEditorBackgroundColor);
            }
            else
            {
                dashboardBeingEdited.ApplyDetails(DashboardEditorTitle, DashboardEditorBackgroundColor);
            }

            CloseDashboardEditor();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            DashboardEditorErrorText = exception.Message;
        }
    }

    private void SaveWidget()
    {
        WidgetEditorErrorText = string.Empty;

        try
        {
            KustoDashboardWidgetLayout layout = widgetBeingEdited is null
                ? CreateNextWidgetLayout()
                : new KustoDashboardWidgetLayout(
                    widgetBeingEdited.Column,
                    widgetBeingEdited.Row,
                    widgetBeingEdited.ColumnSpan,
                    widgetBeingEdited.RowSpan);
            KustoVisualizationKind visualizationKind = WidgetEditorDisplayMode
                == KustoDashboardWidgetDisplayMode.Table
                ? KustoVisualizationKind.Table
                : SelectedVisualizationOption.Kind;
            KustoDashboardWidget definition = new(
                widgetBeingEdited?.Id ?? Guid.NewGuid(),
                WidgetEditorTitle,
                new Uri(WidgetEditorClusterAddress, UriKind.Absolute),
                WidgetEditorDatabaseName,
                WidgetEditorQueryText,
                SelectedRefreshOption.Interval,
                WidgetEditorDisplayMode,
                visualizationKind,
                layout,
                WidgetEditorBackgroundColor,
                WidgetEditorForegroundColor,
                WidgetEditorAccentColor);

            if (widgetBeingEdited is null)
            {
                AddWidget(definition);
            }
            else if (!ReferenceEquals(dashboardContainingEditedWidget, SelectedDashboard))
            {
                KustoDashboardViewModel sourceDashboard = dashboardContainingEditedWidget
                    ?? throw new InvalidOperationException("The widget's source dashboard is unavailable.");
                sourceDashboard.RemoveWidget(widgetBeingEdited);
                AddWidget(definition);
            }
            else
            {
                widgetBeingEdited.ApplyDefinition(definition);
            }

            CloseWidgetEditor();
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException or InvalidOperationException)
        {
            WidgetEditorErrorText = exception.Message;
        }
    }

    private async Task RefreshSelectedFromCommandAsync(CancellationToken cancellationToken)
    {
        await RefreshSelectedAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    private KustoDashboardViewModel CreateDashboardViewModel(KustoDashboard definition)
    {
        return new KustoDashboardViewModel(definition, queryService, _ => Save());
    }

    private void Save()
    {
        dashboardStore.Save(new KustoDashboardCatalog(
            Dashboards.Select(item => item.CreateDefinition())));
    }
}
