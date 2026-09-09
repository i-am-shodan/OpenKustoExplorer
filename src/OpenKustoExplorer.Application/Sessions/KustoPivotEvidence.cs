namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Describes one inferred input-to-output transformation within a recorded result row.
/// </summary>
public sealed class KustoPivotEvidence
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoPivotEvidence"/> class.
    /// </summary>
    /// <param name="executionId">The supporting execution identifier.</param>
    /// <param name="executionSequence">The supporting execution sequence.</param>
    /// <param name="sourceTableName">The optional bound source table.</param>
    /// <param name="inputCoordinate">The input value occurrence.</param>
    /// <param name="outputCoordinate">The output value occurrence.</param>
    /// <param name="inputColumnName">The recorded input column.</param>
    /// <param name="outputColumnName">The recorded output column.</param>
    /// <param name="inputSourceColumnName">The optional source-table input column.</param>
    /// <param name="outputSourceColumnName">The optional source-table output column.</param>
    /// <param name="input">The exact typed input identity.</param>
    /// <param name="output">The exact typed output identity.</param>
    /// <param name="kind">The evidence classification.</param>
    /// <param name="cost">The positive evidence cost.</param>
    public KustoPivotEvidence(
        Guid executionId,
        long executionSequence,
        string? sourceTableName,
        KustoRecordedValueCoordinate inputCoordinate,
        KustoRecordedValueCoordinate outputCoordinate,
        string inputColumnName,
        string outputColumnName,
        string? inputSourceColumnName,
        string? outputSourceColumnName,
        KustoRecordedValueIdentity input,
        KustoRecordedValueIdentity output,
        KustoPivotEvidenceKind kind,
        int cost)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(executionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(executionSequence);
        ArgumentNullException.ThrowIfNull(inputCoordinate);
        ArgumentNullException.ThrowIfNull(outputCoordinate);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputColumnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputColumnName);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cost);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ExecutionId = executionId;
        ExecutionSequence = executionSequence;
        SourceTableName = sourceTableName;
        InputCoordinate = inputCoordinate;
        OutputCoordinate = outputCoordinate;
        InputColumnName = inputColumnName;
        OutputColumnName = outputColumnName;
        InputSourceColumnName = inputSourceColumnName;
        OutputSourceColumnName = outputSourceColumnName;
        Input = input;
        Output = output;
        Kind = kind;
        Cost = cost;
    }

    /// <summary>Gets the supporting execution identifier.</summary>
    public Guid ExecutionId { get; }

    /// <summary>Gets the supporting execution sequence.</summary>
    public long ExecutionSequence { get; }

    /// <summary>Gets the optional bound source table.</summary>
    public string? SourceTableName { get; }

    /// <summary>Gets the input value occurrence.</summary>
    public KustoRecordedValueCoordinate InputCoordinate { get; }

    /// <summary>Gets the output value occurrence.</summary>
    public KustoRecordedValueCoordinate OutputCoordinate { get; }

    /// <summary>Gets the recorded input column.</summary>
    public string InputColumnName { get; }

    /// <summary>Gets the recorded output column.</summary>
    public string OutputColumnName { get; }

    /// <summary>Gets the optional source-table input column.</summary>
    public string? InputSourceColumnName { get; }

    /// <summary>Gets the optional source-table output column.</summary>
    public string? OutputSourceColumnName { get; }

    /// <summary>Gets the exact typed input identity.</summary>
    public KustoRecordedValueIdentity Input { get; }

    /// <summary>Gets the exact typed output identity.</summary>
    public KustoRecordedValueIdentity Output { get; }

    /// <summary>Gets the evidence classification.</summary>
    public KustoPivotEvidenceKind Kind { get; }

    /// <summary>Gets the positive evidence cost.</summary>
    public int Cost { get; }
}
