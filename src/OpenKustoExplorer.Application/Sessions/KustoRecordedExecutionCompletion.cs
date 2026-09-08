using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Contains the final state of one recorded execution.
/// </summary>
public sealed class KustoRecordedExecutionCompletion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedExecutionCompletion"/> class.
    /// </summary>
    /// <param name="status">The final execution status.</param>
    /// <param name="completedAtUtc">The UTC completion time.</param>
    /// <param name="result">The optional successful result.</param>
    /// <param name="errorMessage">The optional failure message.</param>
    public KustoRecordedExecutionCompletion(
        KustoRecordedExecutionStatus status,
        DateTimeOffset completedAtUtc,
        KustoQueryResult? result,
        string? errorMessage)
    {
        if (!Enum.IsDefined(status) || status == KustoRecordedExecutionStatus.Running)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (status == KustoRecordedExecutionStatus.Succeeded && result is null)
        {
            throw new ArgumentException("A successful recorded execution requires a result.", nameof(result));
        }

        Status = status;
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        Result = result;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
    }

    /// <summary>Gets the final status.</summary>
    public KustoRecordedExecutionStatus Status { get; }

    /// <summary>Gets the UTC completion time.</summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>Gets the optional successful result.</summary>
    public KustoQueryResult? Result { get; }

    /// <summary>Gets the optional failure message.</summary>
    public string? ErrorMessage { get; }
}
