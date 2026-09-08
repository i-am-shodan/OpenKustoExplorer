using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one recorded query execution and its paged retained result tables.
/// </summary>
public sealed class KustoRecordedExecutionViewModel : ObservableObject
{
    private const int RowsPerPage = 50;
    private readonly IReadOnlyList<KustoChainEndpoint> endpoints;
    private readonly IReadOnlyList<KustoRecordedInterest> interests;
    private readonly IReadOnlyList<KustoRecordedMark> marks;
    private readonly IReadOnlyList<ResultTableDescriptor> resultTables;
    private readonly string queryTitle;
    private int currentPageIndex;
    private KustoRecordedResultTableViewModel? selectedTable;
    private ReadOnlyCollection<KustoRecordedResultTableViewModel>? tables;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedExecutionViewModel"/> class.
    /// </summary>
    /// <param name="execution">The recorded execution.</param>
    /// <param name="interests">The session's active value interests.</param>
    /// <param name="marks">The session's user marks.</param>
    /// <param name="endpoints">The session's chain endpoints.</param>
    public KustoRecordedExecutionViewModel(
        KustoRecordedExecution execution,
        IReadOnlyList<KustoRecordedInterest> interests,
        IReadOnlyList<KustoRecordedMark> marks,
        IReadOnlyList<KustoChainEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(interests);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(endpoints);
        Execution = execution;
        this.interests = interests;
        this.marks = marks;
        this.endpoints = endpoints;
        resultTables = execution.Result is null
            ? []
            : GetUserResultTables(execution.Result.Tables);
        (InputColumnsText, OutputColumnsText, DiscoveryFlowText) = CreateDiscoveryFlow(
            execution,
            interests,
            resultTables);
        queryTitle = execution.DisplayName ?? CreateAutomaticQueryTitle(
            execution,
            InputColumnsText,
            OutputColumnsText,
            DiscoveryFlowText);
        PreviousPageCommand = new RelayCommand(
            () => SetPage(currentPageIndex - 1),
            () => HasPreviousPage);
        NextPageCommand = new RelayCommand(
            () => SetPage(currentPageIndex + 1),
            () => HasNextPage);
    }

    /// <summary>Occurs after the visible 50-row result page changes.</summary>
    public event EventHandler? PageChanged;

    /// <summary>Gets the recorded execution.</summary>
    public KustoRecordedExecution Execution { get; }

    /// <summary>Gets the execution identifier.</summary>
    public Guid Id => Execution.Id;

    /// <summary>Gets the source query document title.</summary>
    public string DocumentTitle => Execution.DocumentTitle;

    /// <summary>Gets the user-assigned or inferred query title.</summary>
    public string QueryTitle => queryTitle;

    /// <summary>Gets pertinent predicate input columns.</summary>
    public string InputColumnsText { get; }

    /// <summary>Gets pertinent result output columns.</summary>
    public string OutputColumnsText { get; }

    /// <summary>Gets the pertinent input-to-output flow.</summary>
    public string DiscoveryFlowText { get; }

    /// <summary>Gets the executed query text.</summary>
    public string QueryText => Execution.QueryText;

    /// <summary>Gets the executed query as a single-line list preview.</summary>
    public string QueryPreview => QueryText.ReplaceLineEndings(" ").Trim();

    /// <summary>Gets the local start time.</summary>
    public string StartedText => Execution.StartedAtUtc
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Gets the cluster and database target.</summary>
    public string TargetText => $"{Execution.ClusterUri.Host} / {Execution.DatabaseName}";

    /// <summary>Gets the execution status text.</summary>
    public string StatusText => Execution.Status.ToString();

    /// <summary>Gets a concise result summary.</summary>
    public string ResultSummary
    {
        get
        {
            if (Execution.Result is null)
            {
                return Execution.ErrorMessage ?? StatusText;
            }

            int rowCount = resultTables.Sum(table => table.Table.Rows.Count);
            string limitText = Execution.Result.Completeness == KustoQueryResultCompleteness.RecordLimitReached
                ? " retained · result limited"
                : string.Empty;
            return $"{rowCount:N0} rows{limitText} · {Execution.Result.Duration.TotalMilliseconds:N0} ms";
        }
    }

    /// <summary>Gets a heading for the selected query result.</summary>
    public string ResultHeading => $"{QueryTitle} results";

    /// <summary>Gets the result-table slices on the current 50-row page.</summary>
    public ReadOnlyCollection<KustoRecordedResultTableViewModel> Tables
        => tables ??= CreateTables();

    /// <summary>Gets the total number of retained result rows.</summary>
    public int TotalRowCount => resultTables.Sum(table => table.Table.Rows.Count);

    /// <summary>Gets the number of 50-row result pages.</summary>
    public int PageCount => (TotalRowCount + RowsPerPage - 1) / RowsPerPage;

    /// <summary>Gets the current one-based result page number.</summary>
    public int CurrentPageNumber => PageCount == 0 ? 0 : currentPageIndex + 1;

    /// <summary>Gets the current retained row range.</summary>
    public string PageText
    {
        get
        {
            if (TotalRowCount == 0)
            {
                return "No rows";
            }

            int firstRow = (currentPageIndex * RowsPerPage) + 1;
            int lastRow = Math.Min(firstRow + RowsPerPage - 1, TotalRowCount);
            return $"Rows {firstRow:N0}-{lastRow:N0} of {TotalRowCount:N0}";
        }
    }

    /// <summary>Gets a value indicating whether more than one result page exists.</summary>
    public bool HasMultiplePages => PageCount > 1;

    /// <summary>Gets a value indicating whether an earlier result page exists.</summary>
    public bool HasPreviousPage => currentPageIndex > 0;

    /// <summary>Gets a value indicating whether a later result page exists.</summary>
    public bool HasNextPage => currentPageIndex + 1 < PageCount;

    /// <summary>Gets the command that displays the previous 50 retained rows.</summary>
    public IRelayCommand PreviousPageCommand { get; }

    /// <summary>Gets the command that displays the next 50 retained rows.</summary>
    public IRelayCommand NextPageCommand { get; }

    /// <summary>Gets a value indicating whether retained results exist.</summary>
    public bool HasTables => resultTables.Count > 0;

    /// <summary>Gets a value indicating whether any retained result contains rows.</summary>
    public bool HasRows => resultTables.Any(table => table.Table.Rows.Count > 0);

    /// <summary>Gets or sets the selected retained result table.</summary>
    public KustoRecordedResultTableViewModel? SelectedTable
    {
        get => selectedTable ??= Tables.FirstOrDefault();
        set => SetProperty(ref selectedTable, value);
    }

    /// <summary>Gets an accessible execution summary.</summary>
    public string AutomationName => $"{QueryTitle} at {StartedText}, {TargetText}, {ResultSummary}";

    /// <summary>
    /// Finds a retained cell and displays the page that contains it.
    /// </summary>
    /// <param name="tableOrdinal">The zero-based result-table ordinal.</param>
    /// <param name="rowOrdinal">The zero-based row ordinal within the table.</param>
    /// <param name="columnOrdinal">The zero-based column ordinal.</param>
    /// <returns>The visible cell, or <see langword="null"/> when the coordinate is invalid.</returns>
    internal KustoResultCellViewModel? FindCell(
        int tableOrdinal,
        int rowOrdinal,
        int columnOrdinal)
    {
        int tableStart = 0;
        foreach (ResultTableDescriptor descriptor in resultTables)
        {
            if (descriptor.Ordinal == tableOrdinal)
            {
                if (rowOrdinal < 0
                    || rowOrdinal >= descriptor.Table.Rows.Count
                    || columnOrdinal < 0
                    || columnOrdinal >= descriptor.Table.Columns.Count)
                {
                    return null;
                }

                SetPage((tableStart + rowOrdinal) / RowsPerPage);
                KustoRecordedResultTableViewModel? table = Tables.FirstOrDefault(
                    candidate => candidate.TableOrdinal == tableOrdinal);
                if (table is not null && !ReferenceEquals(selectedTable, table))
                {
                    selectedTable = table;
                    OnPropertyChanged(nameof(SelectedTable));
                }

                return table?.Rows.FirstOrDefault(row => row.RowIndex == rowOrdinal)?
                    .Cells.ElementAtOrDefault(columnOrdinal);
            }

            tableStart += descriptor.Table.Rows.Count;
        }

        return null;
    }

    private static IReadOnlyList<ResultTableDescriptor> GetUserResultTables(
        IReadOnlyList<KustoResultTable> tables)
    {
        KustoResultTable? tableOfContents = tables.FirstOrDefault(IsTableOfContents);
        if (tableOfContents is null)
        {
            return tables
                .Select((table, ordinal) => new ResultTableDescriptor(ordinal, table, null))
                .ToArray();
        }

        List<ResultTableDescriptor> results = [];
        foreach (IReadOnlyList<string> values in tableOfContents.Rows.Select(row => row.Values))
        {
            if (values.Count >= 3
                && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ordinal)
                && ordinal >= 0
                && ordinal < tables.Count
                && string.Equals(values[1], "QueryResult", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new ResultTableDescriptor(ordinal, tables[ordinal], values[2]));
            }
        }

        return results;
    }

    private static (string InputColumns, string OutputColumns, string Flow) CreateDiscoveryFlow(
        KustoRecordedExecution execution,
        IReadOnlyList<KustoRecordedInterest> interests,
        IReadOnlyList<ResultTableDescriptor> resultTables)
    {
        string[] inputColumns = interests
            .Where(interest => !interest.IsSuppressed
                && interest.DeclaredExecutionId == execution.Id
                && interest.Source == KustoRecordedInterestSource.QueryPredicate)
            .Select(interest => interest.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        HashSet<KustoRecordedValueIdentity> pertinentIdentities = interests
            .Where(interest => !interest.IsSuppressed)
            .Select(interest => interest.Identity)
            .ToHashSet();
        List<string> outputColumns = [];
        HashSet<string> outputNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> inputNames = new(inputColumns, StringComparer.OrdinalIgnoreCase);
        foreach (KustoResultTable table in resultTables.Select(descriptor => descriptor.Table))
        {
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                KustoResultColumn column = table.Columns[columnIndex];
                bool containsPertinentValue = table.Rows.Any(row => pertinentIdentities.Contains(
                    KustoRecordedValueCanonicalizer.Create(column.TypeName, row.ResultValues[columnIndex])));
                if (containsPertinentValue
                    && !inputNames.Contains(column.Name)
                    && outputNames.Add(column.Name))
                {
                    outputColumns.Add(column.Name);
                }
            }
        }

        string inputText = string.Join(", ", inputColumns);
        string outputText = string.Join(", ", outputColumns);
        string flow = (inputText.Length > 0, outputText.Length > 0) switch
        {
            (true, true) => $"{inputText} → {outputText}",
            (true, false) => $"Filter: {inputText}",
            (false, true) => $"Discovered: {outputText}",
            _ => "Pertinent value",
        };
        return (inputText, outputText, flow);
    }

    private static string CreateAutomaticQueryTitle(
        KustoRecordedExecution execution,
        string inputColumns,
        string outputColumns,
        string discoveryFlow)
    {
        string sourceName = execution.Relation?.SourceTableName
            ?? TryGetLeadingSourceName(execution.QueryText)
            ?? execution.DocumentTitle;
        string detail = outputColumns;
        if (inputColumns.Length > 0)
        {
            detail = outputColumns.Length > 0 ? discoveryFlow : $"by {inputColumns}";
        }

        if (detail.Length == 0 || string.Equals(sourceName, detail, StringComparison.OrdinalIgnoreCase))
        {
            return sourceName;
        }

        return $"{sourceName} · {detail}";
    }

    private static string? TryGetLeadingSourceName(string queryText)
    {
        string? firstLine = queryText.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
        if (firstLine is null
            || firstLine.StartsWith("let ", StringComparison.OrdinalIgnoreCase)
            || firstLine.Contains(';', StringComparison.Ordinal))
        {
            return null;
        }

        int pipeIndex = firstLine.IndexOf('|', StringComparison.Ordinal);
        string candidate = (pipeIndex >= 0 ? firstLine[..pipeIndex] : firstLine).Trim();
        return candidate.Length > 0 && !candidate.Contains(' ', StringComparison.Ordinal)
            ? candidate.Trim('[', ']', '\'', '"')
            : null;
    }

    private static bool IsTableOfContents(KustoResultTable table)
    {
        string[] columnNames = table.Columns.Select(column => column.Name).ToArray();
        return table.Name.StartsWith("Table_", StringComparison.OrdinalIgnoreCase)
            && columnNames.Contains("Ordinal", StringComparer.OrdinalIgnoreCase)
            && columnNames.Contains("Kind", StringComparer.OrdinalIgnoreCase)
            && columnNames.Contains("Name", StringComparer.OrdinalIgnoreCase)
            && columnNames.Contains("Id", StringComparer.OrdinalIgnoreCase)
            && columnNames.Contains("PrettyName", StringComparer.OrdinalIgnoreCase);
    }

    private ReadOnlyCollection<KustoRecordedResultTableViewModel> CreateTables()
    {
        int pageStart = currentPageIndex * RowsPerPage;
        int pageEnd = Math.Min(pageStart + RowsPerPage, TotalRowCount);
        int tableStart = 0;
        List<KustoRecordedResultTableViewModel> visibleTables = [];
        foreach (ResultTableDescriptor descriptor in resultTables)
        {
            int tableEnd = tableStart + descriptor.Table.Rows.Count;
            int visibleStart = Math.Max(pageStart, tableStart);
            int visibleEnd = Math.Min(pageEnd, tableEnd);
            if (visibleStart < visibleEnd)
            {
                visibleTables.Add(new KustoRecordedResultTableViewModel(
                    Execution.Id,
                    descriptor.Ordinal,
                    descriptor.Table,
                    interests,
                    marks,
                    endpoints,
                    descriptor.DisplayName,
                    visibleStart - tableStart,
                    visibleEnd - visibleStart));
            }

            tableStart = tableEnd;
        }

        return visibleTables.AsReadOnly();
    }

    private void SetPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PageCount || pageIndex == currentPageIndex)
        {
            return;
        }

        currentPageIndex = pageIndex;
        tables = CreateTables();
        selectedTable = tables.FirstOrDefault();
        OnPropertyChanged(nameof(Tables));
        OnPropertyChanged(nameof(SelectedTable));
        OnPropertyChanged(nameof(CurrentPageNumber));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        PageChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record ResultTableDescriptor(
        int Ordinal,
        KustoResultTable Table,
        string? DisplayName);
}
