using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Contains one completed scheduled-query execution and its optional result.
/// </summary>
public sealed class KustoAutomationRun
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomationRun"/> class.
    /// </summary>
    /// <param name="id">The stable run identifier.</param>
    /// <param name="startedAtUtc">The UTC execution start.</param>
    /// <param name="completedAtUtc">The UTC execution completion.</param>
    /// <param name="status">The final execution state.</param>
    /// <param name="errorMessage">The optional failure or cancellation message.</param>
    /// <param name="result">The optional materialized result.</param>
    public KustoAutomationRun(
        Guid id,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        KustoAutomationRunStatus status,
        string? errorMessage,
        KustoQueryResult? result)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);

        if (completedAtUtc < startedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAtUtc), "Completion cannot precede the start time.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (status == KustoAutomationRunStatus.Succeeded && result is null)
        {
            throw new ArgumentException("A successful automation run requires a result.", nameof(result));
        }

        Id = id;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        Status = status;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
        Result = result;
    }

    /// <summary>
    /// Gets the stable run identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the UTC execution start.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; }

    /// <summary>
    /// Gets the UTC execution completion.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>
    /// Gets the final execution state.
    /// </summary>
    public KustoAutomationRunStatus Status { get; }

    /// <summary>
    /// Gets the optional failure or cancellation message.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the optional materialized result.
    /// </summary>
    public KustoQueryResult? Result { get; }
}
