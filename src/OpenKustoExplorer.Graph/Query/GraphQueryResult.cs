namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Contains bounded projected rows, matched graph elements, and diagnostics from one openCypher query.
/// </summary>
public sealed class GraphQueryResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQueryResult"/> class.
    /// </summary>
    /// <param name="snapshot">The graph snapshot used for execution.</param>
    /// <param name="queryText">The executed query text.</param>
    /// <param name="columns">Projected columns in query order.</param>
    /// <param name="rows">The bounded projected rows.</param>
    /// <param name="viewport">Matched graph elements suitable for layout.</param>
    /// <param name="diagnostics">Syntax, validation, execution, and truncation diagnostics.</param>
    /// <param name="duration">The query execution duration.</param>
    /// <param name="areRowsTruncated">Whether projected rows exceeded the request limit.</param>
    public GraphQueryResult(
        GraphSnapshot snapshot,
        string queryText,
        IEnumerable<GraphQueryColumn> columns,
        IEnumerable<GraphQueryRow> rows,
        GraphViewport viewport,
        IEnumerable<GraphQueryDiagnostic> diagnostics,
        TimeSpan duration,
        bool areRowsTruncated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        GraphQueryColumn[] columnSnapshot = columns.ToArray();
        GraphQueryRow[] rowSnapshot = rows.ToArray();

        if (rowSnapshot.Any(row => row.Values.Count != columnSnapshot.Length))
        {
            throw new ArgumentException("Every graph query row must match the projected column count.", nameof(rows));
        }

        Snapshot = snapshot;
        QueryText = queryText.Trim();
        Columns = Array.AsReadOnly(columnSnapshot);
        Rows = Array.AsReadOnly(rowSnapshot);
        Viewport = viewport;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        Duration = duration;
        AreRowsTruncated = areRowsTruncated;
    }

    /// <summary>
    /// Gets the graph snapshot used for execution.
    /// </summary>
    public GraphSnapshot Snapshot { get; }

    /// <summary>
    /// Gets the executed query text.
    /// </summary>
    public string QueryText { get; }

    /// <summary>
    /// Gets projected columns in query order.
    /// </summary>
    public IReadOnlyList<GraphQueryColumn> Columns { get; }

    /// <summary>
    /// Gets bounded projected rows.
    /// </summary>
    public IReadOnlyList<GraphQueryRow> Rows { get; }

    /// <summary>
    /// Gets matched graph elements suitable for layout.
    /// </summary>
    public GraphViewport Viewport { get; }

    /// <summary>
    /// Gets syntax, validation, execution, and truncation diagnostics.
    /// </summary>
    public IReadOnlyList<GraphQueryDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets execution duration.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets a value indicating whether projected rows exceeded the request limit.
    /// </summary>
    public bool AreRowsTruncated { get; }

    /// <summary>
    /// Gets a value indicating whether no error diagnostic blocked execution.
    /// </summary>
    public bool Succeeded => Diagnostics.All(
        diagnostic => diagnostic.Severity != GraphQueryDiagnosticSeverity.Error);
}
