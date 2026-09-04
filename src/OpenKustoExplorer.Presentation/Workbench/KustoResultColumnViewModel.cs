using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one column header in a materialized Kusto result table.
/// </summary>
public sealed class KustoResultColumnViewModel : ObservableObject
{
    private const double ApproximateCharacterWidth = 7;
    private const double HeaderChromeWidth = 60;
    private const double HorizontalCellPadding = 24;
    private const double MaximumContentDisplayWidth = 360;
    private const double MinimumDisplayWidth = 84;
    private static readonly ReadOnlyCollection<KustoResultFilterOption> AvailableFilterOptions = Array.AsReadOnly(
        new KustoResultFilterOption[]
        {
            new(KustoResultFilterOperator.Equals, "Equals (==)", true),
            new(KustoResultFilterOperator.NotEquals, "Does not equal (!=)", true),
            new(KustoResultFilterOperator.Contains, "Contains", true),
            new(KustoResultFilterOperator.DoesNotContain, "Does not contain", true),
            new(KustoResultFilterOperator.StartsWith, "Starts with", true),
            new(KustoResultFilterOperator.EndsWith, "Ends with", true),
            new(KustoResultFilterOperator.GreaterThan, "Greater than (>)", true),
            new(KustoResultFilterOperator.GreaterThanOrEqual, "Greater than or equal (>=)", true),
            new(KustoResultFilterOperator.LessThan, "Less than (<)", true),
            new(KustoResultFilterOperator.LessThanOrEqual, "Less than or equal (<=)", true),
            new(KustoResultFilterOperator.IsEmpty, "Is empty", false),
            new(KustoResultFilterOperator.IsNotEmpty, "Is not empty", false),
        });

    private readonly ReadOnlyCollection<KustoResultFilterOption> filterOptions = AvailableFilterOptions;
    private string filterText = string.Empty;
    private KustoResultFilterOption selectedFilterOption = AvailableFilterOptions[2];
    private KustoResultSortDirection sortDirection;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultColumnViewModel"/> class.
    /// </summary>
    /// <param name="column">The immutable result column.</param>
    public KustoResultColumnViewModel(KustoResultColumn column)
        : this(column, 0, 150)
    {
    }

    private KustoResultColumnViewModel(KustoResultColumn column, int columnIndex, double displayWidth)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ColumnIndex = columnIndex;
        Name = column.Name;
        TypeName = column.TypeName;
        DisplayWidth = displayWidth;
    }

    /// <summary>
    /// Gets the zero-based source column index.
    /// </summary>
    public int ColumnIndex { get; }

    /// <summary>
    /// Gets the column name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the server-reported column type.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the content-fitted display width shared by the header and every cell in this column.
    /// </summary>
    public double DisplayWidth { get; }

    /// <summary>
    /// Gets the filter operations available for this column.
    /// </summary>
    public IReadOnlyList<KustoResultFilterOption> FilterOptions => filterOptions;

    /// <summary>
    /// Gets or sets the selected filter operation.
    /// </summary>
    public KustoResultFilterOption SelectedFilterOption
    {
        get => selectedFilterOption;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (SetProperty(ref selectedFilterOption, value))
            {
                OnPropertyChanged(nameof(IsFilterValueRequired));
                OnPropertyChanged(nameof(IsFilterActive));
                OnPropertyChanged(nameof(FilterSummary));
                OnPropertyChanged(nameof(FilterToolTip));
            }
        }
    }

    /// <summary>
    /// Gets or sets the comparison text for this column.
    /// </summary>
    public string FilterText
    {
        get => filterText;
        set
        {
            value ??= string.Empty;

            if (SetProperty(ref filterText, value))
            {
                OnPropertyChanged(nameof(IsFilterActive));
                OnPropertyChanged(nameof(FilterSummary));
                OnPropertyChanged(nameof(FilterToolTip));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the selected operation needs comparison text.
    /// </summary>
    public bool IsFilterValueRequired => SelectedFilterOption.RequiresValue;

    /// <summary>
    /// Gets a value indicating whether this column currently filters rows.
    /// </summary>
    public bool IsFilterActive => !IsFilterValueRequired || FilterText.Length > 0;

    /// <summary>
    /// Gets a concise description of this column's active filter.
    /// </summary>
    public string FilterSummary
    {
        get
        {
            if (!IsFilterActive)
            {
                return "No filter";
            }

            return IsFilterValueRequired
                ? $"{SelectedFilterOption.DisplayName} {FilterText}"
                : SelectedFilterOption.DisplayName;
        }
    }

    /// <summary>
    /// Gets the accessible sort action name.
    /// </summary>
    public string SortAutomationName => $"Sort by {Name}";

    /// <summary>
    /// Gets the accessible filter action name.
    /// </summary>
    public string FilterAutomationName => $"Filter {Name}";

    /// <summary>
    /// Gets filter action guidance including the current operation when active.
    /// </summary>
    public string FilterToolTip => IsFilterActive
        ? $"{FilterAutomationName}: {FilterSummary}"
        : FilterAutomationName;

    /// <summary>
    /// Gets the current sort direction.
    /// </summary>
    public KustoResultSortDirection SortDirection
    {
        get => sortDirection;
        private set
        {
            if (SetProperty(ref sortDirection, value))
            {
                OnPropertyChanged(nameof(IsSortActive));
                OnPropertyChanged(nameof(IsSortAscending));
                OnPropertyChanged(nameof(IsSortDescending));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this column controls result sorting.
    /// </summary>
    public bool IsSortActive => SortDirection != KustoResultSortDirection.None;

    /// <summary>
    /// Gets a value indicating whether this column sorts ascending.
    /// </summary>
    public bool IsSortAscending => SortDirection == KustoResultSortDirection.Ascending;

    /// <summary>
    /// Gets a value indicating whether this column sorts descending.
    /// </summary>
    public bool IsSortDescending => SortDirection == KustoResultSortDirection.Descending;

    /// <summary>
    /// Creates result columns sized from the complete materialized table.
    /// </summary>
    /// <param name="table">The immutable result table.</param>
    /// <returns>Column view models with aligned content-fitted widths.</returns>
    internal static IReadOnlyList<KustoResultColumnViewModel> CreateForTable(KustoResultTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        KustoResultColumnViewModel[] columns = new KustoResultColumnViewModel[table.Columns.Count];

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            KustoResultColumn column = table.Columns[columnIndex];
            int maximumContentCharacterCount = 0;

            foreach (KustoResultRow row in table.Rows)
            {
                maximumContentCharacterCount = Math.Max(
                    maximumContentCharacterCount,
                    GetMaximumLineLength(row.Values[columnIndex]));
            }

            double contentDisplayWidth = Math.Clamp(
                (maximumContentCharacterCount * ApproximateCharacterWidth) + HorizontalCellPadding,
                MinimumDisplayWidth,
                MaximumContentDisplayWidth);
            int maximumHeaderCharacterCount = Math.Max(column.Name.Length, column.TypeName.Length);
            double minimumHeaderWidth = (maximumHeaderCharacterCount * ApproximateCharacterWidth)
                + HeaderChromeWidth;
            double displayWidth = Math.Max(contentDisplayWidth, minimumHeaderWidth);
            columns[columnIndex] = new KustoResultColumnViewModel(column, columnIndex, displayWidth);
        }

        return Array.AsReadOnly(columns);
    }

    /// <summary>
    /// Restores this column's default inactive filter.
    /// </summary>
    internal void ClearFilter()
    {
        SelectedFilterOption = AvailableFilterOptions[2];
        FilterText = string.Empty;
    }

    /// <summary>
    /// Restores server row ordering for this column.
    /// </summary>
    internal void ClearSort()
    {
        SortDirection = KustoResultSortDirection.None;
    }

    /// <summary>
    /// Advances this column through ascending, descending, and server ordering.
    /// </summary>
    internal void CycleSort()
    {
        SortDirection = SortDirection switch
        {
            KustoResultSortDirection.None => KustoResultSortDirection.Ascending,
            KustoResultSortDirection.Ascending => KustoResultSortDirection.Descending,
            _ => KustoResultSortDirection.None,
        };
    }

    private static int GetMaximumLineLength(string text)
    {
        int maximumLength = 0;
        int currentLength = 0;

        foreach (char character in text)
        {
            if (character is '\r' or '\n')
            {
                maximumLength = Math.Max(maximumLength, currentLength);
                currentLength = 0;
            }
            else
            {
                currentLength++;
            }
        }

        return Math.Max(maximumLength, currentLength);
    }
}
