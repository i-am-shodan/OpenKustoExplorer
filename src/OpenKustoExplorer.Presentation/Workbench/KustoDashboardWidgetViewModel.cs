using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Executes and presents one query-backed dashboard widget.
/// </summary>
public sealed class KustoDashboardWidgetViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// The pixel size of one persisted dashboard grid unit.
    /// </summary>
    public const double GridUnitSize = 24;

    private readonly Action<KustoDashboardWidgetViewModel>? definitionChanged;
    private readonly IKustoQueryService queryService;
    private string accentColor;
    private string backgroundColor;
    private int column;
    private int columnSpan;
    private string databaseName;
    private KustoDashboardWidgetDisplayMode displayMode;
    private string errorMessage = string.Empty;
    private bool isDisposed;
    private bool isRefreshing;
    private DateTimeOffset? lastRefreshedAtUtc;
    private DateTimeOffset? nextRefreshAtUtc;
    private string foregroundColor;
    private string queryText;
    private TimeSpan refreshInterval;
    private IReadOnlyList<KustoResultColumnViewModel> resultColumns = [];
    private IReadOnlyList<KustoResultRowViewModel> resultRows = [];
    private string resultSummary = "Waiting for first refresh";
    private int row;
    private int rowSpan;
    private string title;
    private Uri clusterUri;
    private KustoVisualizationKind visualizationKind;
    private KustoVisualizationViewModel? visualization;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardWidgetViewModel"/> class.
    /// </summary>
    /// <param name="definition">The persisted widget definition.</param>
    /// <param name="queryService">The Kusto query service.</param>
    /// <param name="definitionChanged">An optional callback that persists definition changes.</param>
    public KustoDashboardWidgetViewModel(
        KustoDashboardWidget definition,
        IKustoQueryService queryService,
        Action<KustoDashboardWidgetViewModel>? definitionChanged = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(queryService);
        this.queryService = queryService;
        this.definitionChanged = definitionChanged;
        Id = definition.Id;
        title = definition.Title;
        clusterUri = definition.ClusterUri;
        databaseName = definition.DatabaseName;
        queryText = definition.QueryText;
        refreshInterval = definition.RefreshInterval;
        displayMode = definition.DisplayMode;
        visualizationKind = definition.VisualizationKind;
        column = definition.Layout.Column;
        row = definition.Layout.Row;
        columnSpan = definition.Layout.ColumnSpan;
        rowSpan = definition.Layout.RowSpan;
        backgroundColor = definition.BackgroundColor;
        foregroundColor = definition.ForegroundColor;
        accentColor = definition.AccentColor;
        RefreshCommand = new AsyncRelayCommand(RefreshFromCommandAsync);
    }

    /// <summary>
    /// Gets the stable widget identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the widget title.
    /// </summary>
    public string Title => title;

    /// <summary>
    /// Gets the query cluster URI.
    /// </summary>
    public Uri ClusterUri => clusterUri;

    /// <summary>
    /// Gets the query database.
    /// </summary>
    public string DatabaseName => databaseName;

    /// <summary>
    /// Gets the independent KQL query.
    /// </summary>
    public string QueryText => queryText;

    /// <summary>
    /// Gets the automatic refresh interval.
    /// </summary>
    public TimeSpan RefreshInterval => refreshInterval;

    /// <summary>
    /// Gets the human-readable refresh interval.
    /// </summary>
    public string RefreshIntervalText => refreshInterval.TotalMinutes >= 1
        ? $"Every {refreshInterval.TotalMinutes:N0} min"
        : $"Every {refreshInterval.TotalSeconds:N0} sec";

    /// <summary>
    /// Gets how query results are displayed.
    /// </summary>
    public KustoDashboardWidgetDisplayMode DisplayMode => displayMode;

    /// <summary>
    /// Gets the selected visualization kind.
    /// </summary>
    public KustoVisualizationKind VisualizationKind => visualizationKind;

    /// <summary>
    /// Gets the zero-based grid column.
    /// </summary>
    public int Column => column;

    /// <summary>
    /// Gets the zero-based grid row.
    /// </summary>
    public int Row => row;

    /// <summary>
    /// Gets the width in grid units.
    /// </summary>
    public int ColumnSpan => columnSpan;

    /// <summary>
    /// Gets the height in grid units.
    /// </summary>
    public int RowSpan => rowSpan;

    /// <summary>
    /// Gets the pixel x-coordinate used by the dashboard canvas.
    /// </summary>
    public double CanvasLeft => Column * GridUnitSize;

    /// <summary>
    /// Gets the pixel y-coordinate used by the dashboard canvas.
    /// </summary>
    public double CanvasTop => Row * GridUnitSize;

    /// <summary>
    /// Gets the pixel width used by the dashboard canvas.
    /// </summary>
    public double CanvasWidth => ColumnSpan * GridUnitSize;

    /// <summary>
    /// Gets the pixel height used by the dashboard canvas.
    /// </summary>
    public double CanvasHeight => RowSpan * GridUnitSize;

    /// <summary>
    /// Gets the widget background color.
    /// </summary>
    public string BackgroundColor => backgroundColor;

    /// <summary>
    /// Gets the widget text color.
    /// </summary>
    public string ForegroundColor => foregroundColor;

    /// <summary>
    /// Gets the widget accent color.
    /// </summary>
    public string AccentColor => accentColor;

    /// <summary>
    /// Gets a value indicating whether the widget is executing.
    /// </summary>
    public bool IsRefreshing
    {
        get => isRefreshing;
        private set => SetProperty(ref isRefreshing, value);
    }

    /// <summary>
    /// Gets a value indicating whether the widget has a query error.
    /// </summary>
    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>
    /// Gets the most recent query error.
    /// </summary>
    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    /// <summary>
    /// Gets the concise result status.
    /// </summary>
    public string ResultSummary
    {
        get => resultSummary;
        private set => SetProperty(ref resultSummary, value);
    }

    /// <summary>
    /// Gets the most recent successful UTC refresh time.
    /// </summary>
    public DateTimeOffset? LastRefreshedAtUtc
    {
        get => lastRefreshedAtUtc;
        private set
        {
            if (SetProperty(ref lastRefreshedAtUtc, value))
            {
                OnPropertyChanged(nameof(LastRefreshedText));
            }
        }
    }

    /// <summary>
    /// Gets the next planned UTC refresh time.
    /// </summary>
    public DateTimeOffset? NextRefreshAtUtc
    {
        get => nextRefreshAtUtc;
        private set => SetProperty(ref nextRefreshAtUtc, value);
    }

    /// <summary>
    /// Gets the local last-refresh text.
    /// </summary>
    public string LastRefreshedText => LastRefreshedAtUtc is DateTimeOffset refreshedAtUtc
        ? $"Updated {refreshedAtUtc.ToLocalTime():HH:mm:ss}"
        : "Not refreshed";

    /// <summary>
    /// Gets primary result columns.
    /// </summary>
    public IReadOnlyList<KustoResultColumnViewModel> ResultColumns
    {
        get => resultColumns;
        private set
        {
            if (SetProperty(ref resultColumns, value))
            {
                OnPropertyChanged(nameof(HasResultTable));
                OnPropertyChanged(nameof(ResultTableMinimumWidth));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    /// <summary>
    /// Gets primary result rows.
    /// </summary>
    public IReadOnlyList<KustoResultRowViewModel> ResultRows
    {
        get => resultRows;
        private set => SetProperty(ref resultRows, value);
    }

    /// <summary>
    /// Gets the minimum table width needed to preserve readable columns.
    /// </summary>
    public double ResultTableMinimumWidth => ResultColumns.Sum(item => item.DisplayWidth);

    /// <summary>
    /// Gets a value indicating whether a result table is available.
    /// </summary>
    public bool HasResultTable => ResultColumns.Count > 0;

    /// <summary>
    /// Gets the projected visualization.
    /// </summary>
    public KustoVisualizationViewModel? Visualization
    {
        get => visualization;
        private set
        {
            if (SetProperty(ref visualization, value))
            {
                OnPropertyChanged(nameof(HasVisualization));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether visualization content is available.
    /// </summary>
    public bool HasVisualization => Visualization is not null;

    /// <summary>
    /// Gets a value indicating whether the table presentation is active.
    /// </summary>
    public bool IsTable => DisplayMode == KustoDashboardWidgetDisplayMode.Table;

    /// <summary>
    /// Gets a value indicating whether the visualization presentation is active.
    /// </summary>
    public bool IsVisualization => DisplayMode == KustoDashboardWidgetDisplayMode.Visualization;

    /// <summary>
    /// Gets a value indicating whether no result, visualization, or error is available.
    /// </summary>
    public bool ShowEmptyState => !HasError
        && ((IsTable && !HasResultTable) || (IsVisualization && !HasVisualization));

    /// <summary>
    /// Gets the command that immediately refreshes the widget.
    /// </summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Refreshes the widget if its interval has elapsed.
    /// </summary>
    /// <param name="utcNow">The current UTC time.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the refresh check.</returns>
    public Task RefreshIfDueAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        return NextRefreshAtUtc is null || NextRefreshAtUtc <= utcNow
            ? RefreshAsync(utcNow, cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Immediately executes and projects this widget's query.
    /// </summary>
    /// <param name="utcNow">The UTC time used to schedule the next refresh.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing query execution.</returns>
    public async Task RefreshAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        if (IsRefreshing || isDisposed)
        {
            return;
        }

        IsRefreshing = true;
        ErrorMessage = string.Empty;
        ResultSummary = "Refreshing";

        try
        {
            KustoQueryRequest request = new(ClusterUri, DatabaseName, QueryText);
            KustoQueryResult result = await queryService.ExecuteAsync(request, cancellationToken);
            ApplyResult(result);
            LastRefreshedAtUtc = utcNow;
            NextRefreshAtUtc = utcNow.Add(RefreshInterval);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ResultSummary = "Refresh canceled";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            ResultSummary = "Refresh failed";
            NextRefreshAtUtc = utcNow.Add(RefreshInterval);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Updates the transient snapped-grid placement during drag or resize.
    /// </summary>
    /// <param name="newColumn">The zero-based grid column.</param>
    /// <param name="newRow">The zero-based grid row.</param>
    /// <param name="newColumnSpan">The positive width in grid units.</param>
    /// <param name="newRowSpan">The positive height in grid units.</param>
    public void PreviewLayout(int newColumn, int newRow, int newColumnSpan, int newRowSpan)
    {
        KustoDashboardWidgetLayout validatedLayout = new(newColumn, newRow, newColumnSpan, newRowSpan);
        column = validatedLayout.Column;
        row = validatedLayout.Row;
        columnSpan = validatedLayout.ColumnSpan;
        rowSpan = validatedLayout.RowSpan;
        NotifyLayoutChanged();
    }

    /// <summary>
    /// Persists the current snapped-grid placement.
    /// </summary>
    public void CommitLayout()
    {
        definitionChanged?.Invoke(this);
    }

    /// <summary>
    /// Replaces editable query, display, refresh, and theme settings.
    /// </summary>
    /// <param name="definition">The validated replacement definition.</param>
    public void ApplyDefinition(KustoDashboardWidget definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Id != Id)
        {
            throw new ArgumentException("A widget replacement must preserve its identifier.", nameof(definition));
        }

        title = definition.Title;
        clusterUri = definition.ClusterUri;
        databaseName = definition.DatabaseName;
        queryText = definition.QueryText;
        refreshInterval = definition.RefreshInterval;
        displayMode = definition.DisplayMode;
        visualizationKind = definition.VisualizationKind;
        column = definition.Layout.Column;
        row = definition.Layout.Row;
        columnSpan = definition.Layout.ColumnSpan;
        rowSpan = definition.Layout.RowSpan;
        backgroundColor = definition.BackgroundColor;
        foregroundColor = definition.ForegroundColor;
        accentColor = definition.AccentColor;
        NextRefreshAtUtc = null;
        OnPropertyChanged(string.Empty);
        definitionChanged?.Invoke(this);
    }

    /// <summary>
    /// Creates the immutable persistence snapshot.
    /// </summary>
    /// <returns>The current widget definition.</returns>
    public KustoDashboardWidget CreateDefinition()
    {
        return new KustoDashboardWidget(
            Id,
            Title,
            ClusterUri,
            DatabaseName,
            QueryText,
            RefreshInterval,
            DisplayMode,
            VisualizationKind,
            new KustoDashboardWidgetLayout(Column, Row, ColumnSpan, RowSpan),
            BackgroundColor,
            ForegroundColor,
            AccentColor);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            RefreshCommand.Cancel();
        }
    }

    private async Task RefreshFromCommandAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    private void ApplyResult(KustoQueryResult result)
    {
        KustoResultTable? table = result.Tables.Count > 0 ? result.Tables[0] : null;
        Visualization = null;

        if (table is null)
        {
            ResultColumns = [];
            ResultRows = [];
            ResultSummary = $"No tabular result · {result.Duration.TotalMilliseconds.ToString("N0", CultureInfo.CurrentCulture)} ms";
            return;
        }

        ResultColumns = KustoResultColumnViewModel.CreateForTable(table);
        double[] columnWidths = ResultColumns.Select(item => item.DisplayWidth).ToArray();
        ResultRows = Array.AsReadOnly(table.Rows
            .Select((item, index) => new KustoResultRowViewModel(item, index, table.Columns, columnWidths))
            .ToArray());
        ResultSummary = $"{table.Rows.Count:N0} rows · {result.Duration.TotalMilliseconds:N0} ms";

        if (IsVisualization)
        {
            KustoVisualizationViewModel.TryCreate(
                table,
                new KustoVisualization(VisualizationKind),
                out KustoVisualizationViewModel? createdVisualization,
                out string visualizationMessage);
            Visualization = createdVisualization;

            if (Visualization is null)
            {
                ResultSummary = visualizationMessage;
            }
        }
    }

    private void NotifyLayoutChanged()
    {
        OnPropertyChanged(nameof(Column));
        OnPropertyChanged(nameof(Row));
        OnPropertyChanged(nameof(ColumnSpan));
        OnPropertyChanged(nameof(RowSpan));
        OnPropertyChanged(nameof(CanvasLeft));
        OnPropertyChanged(nameof(CanvasTop));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }
}
