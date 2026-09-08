using System.Collections.ObjectModel;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one retained result table with recording annotations.
/// </summary>
public sealed class KustoRecordedResultTableViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedResultTableViewModel"/> class.
    /// </summary>
    /// <param name="executionId">The owning execution identifier.</param>
    /// <param name="tableOrdinal">The zero-based result-table position.</param>
    /// <param name="table">The retained result table.</param>
    /// <param name="interests">The session's active value interests.</param>
    /// <param name="marks">The session's user marks.</param>
    /// <param name="endpoints">The session's chain endpoints.</param>
    /// <param name="displayName">The optional logical result name.</param>
    /// <param name="firstRowOrdinal">The first retained row included in this page.</param>
    /// <param name="rowCount">The number of retained rows included in this page.</param>
    public KustoRecordedResultTableViewModel(
        Guid executionId,
        int tableOrdinal,
        KustoResultTable table,
        IReadOnlyList<KustoRecordedInterest> interests,
        IReadOnlyList<KustoRecordedMark> marks,
        IReadOnlyList<KustoChainEndpoint> endpoints,
        string? displayName = null,
        int firstRowOrdinal = 0,
        int? rowCount = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(tableOrdinal);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(interests);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentOutOfRangeException.ThrowIfNegative(firstRowOrdinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstRowOrdinal, table.Rows.Count);

        int visibleRowCount = rowCount ?? table.Rows.Count - firstRowOrdinal;
        ArgumentOutOfRangeException.ThrowIfNegative(visibleRowCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            visibleRowCount,
            table.Rows.Count - firstRowOrdinal);

        ExecutionId = executionId;
        TableOrdinal = tableOrdinal;
        TotalRowCount = table.Rows.Count;
        Name = table.Name;
        string fallbackDisplayName = table.Name switch
        {
            "PrimaryResult" => "Results",
            _ when table.Name.StartsWith("Table_", StringComparison.OrdinalIgnoreCase)
                => $"Result {tableOrdinal + 1:N0}",
            _ => table.Name,
        };
        string resolvedDisplayName = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : fallbackDisplayName;
        DisplayName = string.Equals(resolvedDisplayName, "PrimaryResult", StringComparison.OrdinalIgnoreCase)
            ? "Results"
            : resolvedDisplayName;
        Columns = KustoResultColumnViewModel.CreateForTable(table);
        double[] widths = Columns.Select(column => column.DisplayWidth).ToArray();
        HashSet<KustoRecordedValueIdentity> interestingValues = interests
            .Where(interest => !interest.IsSuppressed)
            .Select(interest => interest.Identity)
            .ToHashSet();
        KustoRecordedInterest[] manualInterests = interests
            .Where(interest => interest.Source is KustoRecordedInterestSource.ManualCell
                    or KustoRecordedInterestSource.ConfirmedPredicate
                && !interest.IsSuppressed
                && !interest.Identity.IsNull
                && interest.Identity.CanonicalValue.Length > 0)
            .ToArray();
        HashSet<CoordinateKey> cellMarks = marks
            .Where(mark => mark.Kind == KustoRecordedMarkKind.Cell)
            .Select(mark => CoordinateKey.Create(mark.Coordinate))
            .ToHashSet();
        CoordinateKey? start = endpoints
            .FirstOrDefault(endpoint => endpoint.Role == KustoChainEndpointRole.Start) is { } startEndpoint
                ? CoordinateKey.Create(startEndpoint.Coordinate)
                : null;
        CoordinateKey? destination = endpoints
            .FirstOrDefault(endpoint => endpoint.Role == KustoChainEndpointRole.End) is { } endEndpoint
                ? CoordinateKey.Create(endEndpoint.Coordinate)
                : null;
        int lastRowOrdinal = firstRowOrdinal + visibleRowCount;
        List<KustoResultRowViewModel> rows = new(visibleRowCount);
        for (int rowOrdinal = firstRowOrdinal; rowOrdinal < lastRowOrdinal; rowOrdinal++)
        {
            KustoResultRowViewModel row = new(table.Rows[rowOrdinal], rowOrdinal, table.Columns, widths);
            foreach (KustoResultCellViewModel cell in row.Cells)
            {
                CoordinateKey coordinate = new(executionId, tableOrdinal, rowOrdinal, cell.ColumnIndex);
                KustoRecordedValueIdentity identity = KustoRecordedValueCanonicalizer.Create(cell.TypeName, cell.Value);
                KustoRecordedInterest? manualInterest = !cell.Value.IsNull
                    ? manualInterests.FirstOrDefault(interest => cell.Text.Contains(
                        interest.Identity.CanonicalValue,
                        StringComparison.OrdinalIgnoreCase))
                    : null;
                KustoRecordedValueIdentity colorIdentity = manualInterest?.Identity ?? identity;
                KustoRecordedValueColor color = KustoRecordedValueColorPalette.GetColor(colorIdentity);
                cell.SetRecordingAnnotation(
                    interestingValues.Contains(identity),
                    cellMarks.Contains(coordinate),
                    start == coordinate,
                    destination == coordinate,
                    manualInterest is not null,
                    color.AccentHex,
                    color.HighlightHex);
            }

            rows.Add(row);
        }

        Rows = rows.AsReadOnly();
        string visibleColumns = string.Join(", ", Columns.Take(3).Select(column => column.Name));
        if (Columns.Count > 3)
        {
            visibleColumns += $", +{Columns.Count - 3:N0}";
        }

        MetadataText = $"{RowCountText} · {Columns.Count:N0} columns · {visibleColumns}";
    }

    /// <summary>Gets the owning execution identifier.</summary>
    public Guid ExecutionId { get; }

    /// <summary>Gets the zero-based result-table position.</summary>
    public int TableOrdinal { get; }

    /// <summary>Gets the table name.</summary>
    public string Name { get; }

    /// <summary>Gets a readable result label that hides protocol frame names.</summary>
    public string DisplayName { get; }

    /// <summary>Gets a concise row and column description.</summary>
    public string MetadataText { get; }

    /// <summary>Gets result columns.</summary>
    public IReadOnlyList<KustoResultColumnViewModel> Columns { get; }

    /// <summary>Gets the total number of retained rows in the result table.</summary>
    public int TotalRowCount { get; }

    /// <summary>Gets annotated result rows on the current execution page.</summary>
    public ReadOnlyCollection<KustoResultRowViewModel> Rows { get; }

    /// <summary>Gets the minimum table width.</summary>
    public double MinimumWidth => Columns.Sum(column => column.DisplayWidth);

    /// <summary>Gets the retained row count text.</summary>
    public string RowCountText => TotalRowCount == 1 ? "1 row" : $"{TotalRowCount:N0} rows";

    /// <summary>Gets a value indicating whether the table contains retained rows.</summary>
    public bool HasRows => Rows.Count > 0;

    private readonly record struct CoordinateKey(
        Guid ExecutionId,
        int TableOrdinal,
        int RowOrdinal,
        int ColumnOrdinal)
    {
        internal static CoordinateKey Create(KustoRecordedValueCoordinate coordinate)
        {
            return new CoordinateKey(
                coordinate.ExecutionId,
                coordinate.TableOrdinal,
                coordinate.RowOrdinal,
                coordinate.ColumnOrdinal);
        }
    }
}
