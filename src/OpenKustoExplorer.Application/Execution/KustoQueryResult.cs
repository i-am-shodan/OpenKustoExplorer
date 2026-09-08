namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Contains all result tables materialized for one completed query.
/// </summary>
public sealed class KustoQueryResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoQueryResult"/> class.
    /// </summary>
    /// <param name="tables">The result tables in server order.</param>
    /// <param name="duration">The elapsed authentication and execution duration.</param>
    /// <param name="visualization">The optional server-provided render instructions.</param>
    /// <param name="completeness">Whether the materialized result reached its record budget.</param>
    public KustoQueryResult(
        IEnumerable<KustoResultTable> tables,
        TimeSpan duration,
        KustoVisualization? visualization = null,
        KustoQueryResultCompleteness completeness = KustoQueryResultCompleteness.Complete)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        if (!Enum.IsDefined(completeness))
        {
            throw new ArgumentOutOfRangeException(nameof(completeness));
        }

        Tables = Array.AsReadOnly(tables.ToArray());
        Duration = duration;
        Visualization = visualization;
        Completeness = completeness;
    }

    /// <summary>
    /// Gets the result tables in server order.
    /// </summary>
    public IReadOnlyList<KustoResultTable> Tables { get; }

    /// <summary>
    /// Gets the elapsed authentication and execution duration.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the optional server-provided render instructions.
    /// </summary>
    public KustoVisualization? Visualization { get; }

    /// <summary>
    /// Gets whether the materialized result reached its record budget.
    /// </summary>
    public KustoQueryResultCompleteness Completeness { get; }
}
