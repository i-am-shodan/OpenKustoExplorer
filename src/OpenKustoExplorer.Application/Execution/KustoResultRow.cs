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
        ResultValues = Array.AsReadOnly(values
            .Select(value => new KustoResultValue(value, null, false))
            .ToArray());
        Values = Array.AsReadOnly(ResultValues.Select(value => value.DisplayText).ToArray());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultRow"/> class.
    /// </summary>
    /// <param name="values">The typed result values in column order.</param>
    public KustoResultRow(IEnumerable<KustoResultValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ResultValues = Array.AsReadOnly(values.ToArray());
        Values = Array.AsReadOnly(ResultValues.Select(value => value.DisplayText).ToArray());
    }

    /// <summary>
    /// Gets the display values in column order.
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// Gets typed result values in column order.
    /// </summary>
    public IReadOnlyList<KustoResultValue> ResultValues { get; }
}
