using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Contains metadata needed to begin one recorded execution.
/// </summary>
public sealed class KustoRecordedExecutionStart
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedExecutionStart"/> class.
    /// </summary>
    /// <param name="periodId">The recording-period identifier.</param>
    /// <param name="documentId">The source document identifier.</param>
    /// <param name="documentTitle">The source document title.</param>
    /// <param name="request">The executed query request.</param>
    /// <param name="startedAtUtc">The UTC start time.</param>
    /// <param name="predicateInterests">Predicate-derived interests.</param>
    /// <param name="relation">The optional conservative relation descriptor.</param>
    public KustoRecordedExecutionStart(
        Guid periodId,
        Guid documentId,
        string documentTitle,
        KustoQueryRequest request,
        DateTimeOffset startedAtUtc,
        IEnumerable<KustoPredicateInterest> predicateInterests,
        KustoRecordedRelationDescriptor? relation)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(periodId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(documentId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentTitle);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(predicateInterests);
        PeriodId = periodId;
        DocumentId = documentId;
        DocumentTitle = documentTitle.Trim();
        Request = request;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        PredicateInterests = Array.AsReadOnly(predicateInterests.ToArray());
        Relation = relation;
    }

    /// <summary>Gets the recording-period identifier.</summary>
    public Guid PeriodId { get; }

    /// <summary>Gets the source document identifier.</summary>
    public Guid DocumentId { get; }

    /// <summary>Gets the source document title.</summary>
    public string DocumentTitle { get; }

    /// <summary>Gets the executed query request.</summary>
    public KustoQueryRequest Request { get; }

    /// <summary>Gets the UTC start time.</summary>
    public DateTimeOffset StartedAtUtc { get; }

    /// <summary>Gets predicate-derived interests.</summary>
    public IReadOnlyList<KustoPredicateInterest> PredicateInterests { get; }

    /// <summary>Gets the optional conservative relation descriptor.</summary>
    public KustoRecordedRelationDescriptor? Relation { get; }
}
