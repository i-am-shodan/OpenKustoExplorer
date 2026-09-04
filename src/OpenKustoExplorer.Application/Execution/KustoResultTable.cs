namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Contains one materialized tabular result set.
/// </summary>
public sealed class KustoResultTable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultTable"/> class.
    /// </summary>
    /// <param name="name">The result table display name.</param>
    /// <param name="columns">The result columns in server order.</param>
    /// <param name="rows">The materialized rows in server order.</param>
    public KustoResultTable(
        string name,
        IEnumerable<KustoResultColumn> columns,
        IEnumerable<KustoResultRow> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        Name = name;
        Columns = Array.AsReadOnly(columns.ToArray());
        Rows = Array.AsReadOnly(rows.ToArray());
    }

    /// <summary>
    /// Gets the result table display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the result columns in server order.
    /// </summary>
    public IReadOnlyList<KustoResultColumn> Columns { get; }

    /// <summary>
    /// Gets the materialized rows in server order.
    /// </summary>
    public IReadOnlyList<KustoResultRow> Rows { get; }
}
