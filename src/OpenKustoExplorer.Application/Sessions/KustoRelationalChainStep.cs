namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Describes one source relation retained in a minimized generated-query plan.
/// </summary>
public sealed class KustoRelationalChainStep
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRelationalChainStep"/> class.
    /// </summary>
    /// <param name="executionId">The supporting execution identifier.</param>
    /// <param name="queryText">The supporting recorded query.</param>
    /// <param name="usesQueryPipeline">Whether generation requires the supporting query pipeline.</param>
    /// <param name="sourceTableName">The bound source table.</param>
    /// <param name="inputColumnName">The source-table input column.</param>
    /// <param name="outputColumnName">The source-table output column.</param>
    /// <param name="input">The exact typed input identity.</param>
    /// <param name="output">The exact typed output identity.</param>
    public KustoRelationalChainStep(
        Guid executionId,
        string queryText,
        bool usesQueryPipeline,
        string sourceTableName,
        string inputColumnName,
        string outputColumnName,
        KustoRecordedValueIdentity input,
        KustoRecordedValueIdentity output)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputColumnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputColumnName);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ExecutionId = executionId;
        QueryText = queryText;
        UsesQueryPipeline = usesQueryPipeline;
        SourceTableName = sourceTableName.Trim();
        InputColumnName = inputColumnName.Trim();
        OutputColumnName = outputColumnName.Trim();
        Input = input;
        Output = output;
    }

    /// <summary>Gets the supporting execution identifier.</summary>
    public Guid ExecutionId { get; }

    /// <summary>Gets the supporting recorded query.</summary>
    public string QueryText { get; }

    /// <summary>Gets a value indicating whether generation requires the supporting query pipeline.</summary>
    public bool UsesQueryPipeline { get; }

    /// <summary>Gets the bound source table.</summary>
    public string SourceTableName { get; }

    /// <summary>Gets the source-table input column.</summary>
    public string InputColumnName { get; }

    /// <summary>Gets the source-table output column.</summary>
    public string OutputColumnName { get; }

    /// <summary>Gets the exact typed input identity.</summary>
    public KustoRecordedValueIdentity Input { get; }

    /// <summary>Gets the exact typed output identity.</summary>
    public KustoRecordedValueIdentity Output { get; }
}
