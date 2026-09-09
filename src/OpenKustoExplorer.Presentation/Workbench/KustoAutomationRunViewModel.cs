using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Diagnostics;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one scheduled query execution with shared table and visualization outputs.
/// </summary>
public sealed class KustoAutomationRunViewModel : ObservableObject
{
    private readonly Lazy<KustoVisualization?> fallbackVisualization;
    private readonly KustoAutomationRun run;
    private readonly KustoResultTable? primaryTable;
    private IReadOnlyList<KustoResultColumnViewModel>? resultColumns;
    private IReadOnlyList<KustoResultRowViewModel>? resultRows;
    private int? rowDelta;
    private int selectedOutputTabIndex;
    private KustoVisualizationViewModel? visualization;
    private IReadOnlyList<KustoVisualizationChoiceViewModel>? visualizationChoices;
    private bool visualizationInitialized;
    private string visualizationMessage = "No visualization available";

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomationRunViewModel"/> class.
    /// </summary>
    /// <param name="run">The immutable completed run.</param>
    /// <param name="fallbackVisualization">Render instructions recovered from scheduled KQL.</param>
    public KustoAutomationRunViewModel(
        KustoAutomationRun run,
        KustoVisualization? fallbackVisualization)
        : this(run, new Lazy<KustoVisualization?>(() => fallbackVisualization))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomationRunViewModel"/> class with a shared lazy fallback.
    /// </summary>
    /// <param name="run">The immutable completed run.</param>
    /// <param name="fallbackVisualization">The lazily recovered scheduled KQL render instructions.</param>
    internal KustoAutomationRunViewModel(
        KustoAutomationRun run,
        Lazy<KustoVisualization?> fallbackVisualization)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(fallbackVisualization);

        this.run = run;
        this.fallbackVisualization = fallbackVisualization;
        primaryTable = run.Result is { Tables.Count: > 0 } ? run.Result.Tables[0] : null;
        RenderVisualizationCommand = new RelayCommand<string>(RenderVisualization, _ => primaryTable is not null);
    }

    /// <summary>
    /// Gets the stable run identifier.
    /// </summary>
    public Guid Id => run.Id;

    /// <summary>
    /// Gets the final status.
    /// </summary>
    public KustoAutomationRunStatus Status => run.Status;

    /// <summary>
    /// Gets the local execution start text.
    /// </summary>
    public string StartedText => run.StartedAtUtc
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the concise run summary.
    /// </summary>
    public string SummaryText => run.Result is null
        ? run.ErrorMessage ?? Status.ToString()
        : $"{run.Result.Tables.Sum(table => table.Rows.Count):N0} rows · {run.Result.Duration.TotalMilliseconds:N0} ms";

    /// <summary>
    /// Gets the total rows across all result tables.
    /// </summary>
    public int RowCount => run.Result?.Tables.Sum(table => table.Rows.Count) ?? 0;

    /// <summary>
    /// Gets a value indicating whether this run has a successful predecessor for comparison.
    /// </summary>
    public bool HasRowDelta => rowDelta is not null;

    /// <summary>
    /// Gets the signed row-count change from the previous successful run.
    /// </summary>
    public string RowDeltaText => rowDelta is int delta ? $"{delta:+#;-#;0} rows" : string.Empty;

    /// <summary>
    /// Gets the semantic row-delta color.
    /// </summary>
    public string RowDeltaColorHex => rowDelta switch
    {
        > 0 => "#27864A",
        < 0 => "#D64545",
        _ => "#6B7280",
    };

    /// <summary>
    /// Gets the optional error message.
    /// </summary>
    public string? ErrorMessage => run.ErrorMessage;

    /// <summary>
    /// Gets result columns from the primary table.
    /// </summary>
    public IReadOnlyList<KustoResultColumnViewModel> ResultColumns =>
        resultColumns ??= primaryTable is null
            ? []
            : KustoResultColumnViewModel.CreateForTable(primaryTable);

    /// <summary>
    /// Gets result rows from the primary table.
    /// </summary>
    public IReadOnlyList<KustoResultRowViewModel> ResultRows => resultRows ??= CreateResultRows();

    /// <summary>
    /// Gets the minimum result table width needed to keep columns readable.
    /// </summary>
    public double ResultTableMinimumWidth => ResultColumns.Sum(column => column.DisplayWidth);

    /// <summary>
    /// Gets the visualization choices shared with normal query output.
    /// </summary>
    public IReadOnlyList<KustoVisualizationChoiceViewModel> VisualizationChoices =>
        visualizationChoices ??= KustoVisualizationChoiceViewModel.CreateAll(RenderVisualization);

    /// <summary>
    /// Gets a value indicating whether a primary result table is available.
    /// </summary>
    public bool HasResultTable => ResultColumns.Count > 0;

    /// <summary>
    /// Gets the current visualization.
    /// </summary>
    public KustoVisualizationViewModel? Visualization
    {
        get
        {
            EnsureVisualizationInitialized();
            return visualization;
        }

        private set
        {
            visualizationInitialized = true;
            if (SetProperty(ref visualization, value))
            {
                OnPropertyChanged(nameof(HasVisualization));
                OnPropertyChanged(nameof(ShowVisualizationChoices));
                OnPropertyChanged(nameof(ShowVisualizationEmptyState));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether visualization content is available.
    /// </summary>
    public bool HasVisualization => Visualization is not null;

    /// <summary>
    /// Gets a value indicating whether direct visualization choices should be shown.
    /// </summary>
    public bool ShowVisualizationChoices => HasResultTable && !HasVisualization;

    /// <summary>
    /// Gets a value indicating whether visualization guidance is visible.
    /// </summary>
    public bool ShowVisualizationEmptyState => !HasVisualization;

    /// <summary>
    /// Gets visualization status or validation guidance.
    /// </summary>
    public string VisualizationMessage
    {
        get
        {
            EnsureVisualizationInitialized();
            return visualizationMessage;
        }

        private set => SetProperty(ref visualizationMessage, value);
    }

    /// <summary>
    /// Gets or sets the selected table/visualization output tab.
    /// </summary>
    public int SelectedOutputTabIndex
    {
        get => selectedOutputTabIndex;
        set => SetProperty(ref selectedOutputTabIndex, value);
    }

    /// <summary>
    /// Gets the command that manually renders a selected visualization type.
    /// </summary>
    public IRelayCommand<string> RenderVisualizationCommand { get; }

    /// <summary>
    /// Creates the immutable persistence snapshot.
    /// </summary>
    /// <returns>The source completed run.</returns>
    internal KustoAutomationRun CreateRun()
    {
        return run;
    }

    /// <summary>
    /// Compares this run's row count with the preceding chronological run.
    /// </summary>
    /// <param name="previousRun">The preceding chronological run.</param>
    internal void SetPreviousRun(KustoAutomationRunViewModel? previousRun)
    {
        rowDelta = Status == KustoAutomationRunStatus.Succeeded
            && previousRun?.Status == KustoAutomationRunStatus.Succeeded
            ? RowCount - previousRun.RowCount
            : null;
        OnPropertyChanged(nameof(HasRowDelta));
        OnPropertyChanged(nameof(RowDeltaText));
        OnPropertyChanged(nameof(RowDeltaColorHex));
    }

    private static ReadOnlyCollection<KustoResultRowViewModel> CreateRows(
        KustoResultTable? table,
        IReadOnlyList<double> columnWidths)
    {
        KustoResultRowViewModel[] rows = [];

        if (table is not null)
        {
            rows = table.Rows
                .Select((row, index) => new KustoResultRowViewModel(
                    row,
                    index,
                    table.Columns,
                    columnWidths))
                .ToArray();
        }

        return Array.AsReadOnly(rows);
    }

    private ReadOnlyCollection<KustoResultRowViewModel> CreateResultRows()
    {
        using KustoPerformanceTrace.OperationScope performanceScope = KustoPerformanceTrace.Measure(
            "automation.run.rows.project",
            primaryTable?.Rows.Count ?? 0);
        return CreateRows(primaryTable, ResultColumns.Select(column => column.DisplayWidth).ToArray());
    }

    private void EnsureVisualizationInitialized()
    {
        if (visualizationInitialized)
        {
            return;
        }

        visualizationInitialized = true;
        KustoVisualization? effectiveVisualization = run.Result?.Visualization ?? fallbackVisualization.Value;
        if (primaryTable is not null && effectiveVisualization is not null)
        {
            KustoVisualizationViewModel.TryCreate(
                primaryTable,
                effectiveVisualization,
                out visualization,
                out visualizationMessage);
            selectedOutputTabIndex = 1;
        }
    }

    private void RenderVisualization(string? visualizationName)
    {
        bool hasKind = Enum.TryParse(
            visualizationName,
            ignoreCase: true,
            out KustoVisualizationKind kind)
            && Enum.IsDefined(kind);

        if (primaryTable is not null && hasKind && kind != KustoVisualizationKind.Table)
        {
            ApplyVisualization(new KustoVisualization(kind));
        }
        else if (kind == KustoVisualizationKind.Table)
        {
            SelectedOutputTabIndex = 0;
        }
    }

    private void RenderVisualization(KustoVisualizationKind kind)
    {
        if (primaryTable is not null)
        {
            ApplyVisualization(new KustoVisualization(kind));
        }
    }

    private void ApplyVisualization(KustoVisualization instructions)
    {
        if (primaryTable is not null)
        {
            visualizationInitialized = true;
            KustoVisualizationViewModel.TryCreate(
                primaryTable,
                instructions,
                out KustoVisualizationViewModel? createdVisualization,
                out string message);
            Visualization = createdVisualization;
            VisualizationMessage = message;
            SelectedOutputTabIndex = 1;
        }
    }
}
