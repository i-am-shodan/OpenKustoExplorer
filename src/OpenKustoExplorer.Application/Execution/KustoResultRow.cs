namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Contains the display values for one Kusto result row.
/// </summary>
public sealed class KustoResultRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultRow"/> class.
    /// </summary>
    /// <param name="values">The display values in column order.</param>
    public KustoResultRow(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = Array.AsReadOnly(values.ToArray());
    }

    /// <summary>
    /// Gets the display values in column order.
    /// </summary>
    public IReadOnlyList<string> Values { get; }
}
