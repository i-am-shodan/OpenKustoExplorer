using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Contains one recorded query execution and its retained result.
/// </summary>
public sealed class KustoRecordedExecution
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedExecution"/> class.
    /// </summary>
    /// <param name="id">The execution identifier.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="periodId">The recording-period identifier.</param>
    /// <param name="sequence">The monotonically increasing session sequence.</param>
    /// <param name="documentId">The source document identifier.</param>
    /// <param name="documentTitle">The source document title.</param>
    /// <param name="clusterUri">The target cluster.</param>
    /// <param name="databaseName">The target database.</param>
    /// <param name="queryText">The executed KQL text.</param>
    /// <param name="startedAtUtc">The UTC start time.</param>
    /// <param name="completedAtUtc">The optional UTC completion time.</param>
    /// <param name="status">The execution status.</param>
    /// <param name="errorMessage">The optional failure message.</param>
    /// <param name="result">The optional retained result.</param>
    /// <param name="relation">The optional conservative relation descriptor.</param>
    /// <param name="displayName">The optional user-assigned display name.</param>
    public KustoRecordedExecution(
        Guid id,
        Guid sessionId,
        Guid periodId,
        long sequence,
        Guid documentId,
        string documentTitle,
        Uri clusterUri,
        string databaseName,
        string queryText,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? completedAtUtc,
        KustoRecordedExecutionStatus status,
        string? errorMessage,
        KustoQueryResult? result,
        KustoRecordedRelationDescriptor? relation,
        string? displayName = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(periodId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfEqual(documentId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentTitle);
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Id = id;
        SessionId = sessionId;
        PeriodId = periodId;
        Sequence = sequence;
        DocumentId = documentId;
        DocumentTitle = documentTitle.Trim();
        ClusterUri = clusterUri;
        DatabaseName = databaseName.Trim();
        QueryText = queryText;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        CompletedAtUtc = completedAtUtc?.ToUniversalTime();
        Status = status;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
        Result = result;
        Relation = relation;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    }

    /// <summary>Gets the execution identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the session identifier.</summary>
    public Guid SessionId { get; }

    /// <summary>Gets the recording-period identifier.</summary>
    public Guid PeriodId { get; }

    /// <summary>Gets the monotonically increasing session sequence.</summary>
    public long Sequence { get; }

    /// <summary>Gets the source document identifier.</summary>
    public Guid DocumentId { get; }

    /// <summary>Gets the source document title.</summary>
    public string DocumentTitle { get; }

    /// <summary>Gets the target cluster.</summary>
    public Uri ClusterUri { get; }

    /// <summary>Gets the target database.</summary>
    public string DatabaseName { get; }

    /// <summary>Gets the executed KQL text.</summary>
    public string QueryText { get; }

    /// <summary>Gets the UTC start time.</summary>
    public DateTimeOffset StartedAtUtc { get; }

    /// <summary>Gets the optional UTC completion time.</summary>
    public DateTimeOffset? CompletedAtUtc { get; }

    /// <summary>Gets the execution status.</summary>
    public KustoRecordedExecutionStatus Status { get; }

    /// <summary>Gets the optional failure message.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Gets the optional retained result.</summary>
    public KustoQueryResult? Result { get; }

    /// <summary>Gets the optional conservative relation descriptor.</summary>
    public KustoRecordedRelationDescriptor? Relation { get; }

    /// <summary>Gets the optional user-assigned display name.</summary>
    public string? DisplayName { get; }
}
