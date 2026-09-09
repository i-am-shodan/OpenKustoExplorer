using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one complete recorded session and its query timeline.
/// </summary>
public sealed class KustoRecordedSessionViewModel : ObservableObject
{
    private KustoRecordedExecutionViewModel? selectedExecution;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedSessionViewModel"/> class.
    /// </summary>
    /// <param name="session">The complete recorded session.</param>
    public KustoRecordedSessionViewModel(KustoRecordedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        Executions = new ReadOnlyCollection<KustoRecordedExecutionViewModel>(session.Executions
            .OrderBy(execution => execution.Sequence)
            .Select(execution => new KustoRecordedExecutionViewModel(
                execution,
                session.Interests,
                session.Marks,
                session.Endpoints))
            .ToArray());
        IReadOnlyDictionary<Guid, string> queryTitles = Executions.ToDictionary(
            execution => execution.Id,
            execution => execution.QueryTitle);
        IReadOnlyDictionary<Guid, long> querySequences = session.Executions.ToDictionary(
            execution => execution.Id,
            execution => execution.Sequence);
        KustoRecordedPertinentValueViewModel[] pertinentValues = session.Interests
            .Where(interest => !interest.IsSuppressed)
            .GroupBy(interest => interest.Identity)
            .Select(group => new KustoRecordedPertinentValueViewModel(
                group.OrderBy(interest => querySequences.GetValueOrDefault(interest.DeclaredExecutionId)),
                queryTitles,
                session.Endpoints))
            .OrderByDescending(value => value.IsAddedByUser)
            .ThenBy(value => value.ValueText, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        PertinentValues = new ReadOnlyCollection<KustoRecordedPertinentValueViewModel>(pertinentValues);
        Dictionary<KustoRecordedValueIdentity, KustoRecordedPertinentValueViewModel> valuesByIdentity =
            pertinentValues.ToDictionary(value => value.Identity);
        Dictionary<Guid, KustoRecordedExecutionViewModel> executionsById = Executions.ToDictionary(
            execution => execution.Id);
        TimelineValues = new ReadOnlyCollection<KustoRecordedTimelineValueViewModel>(session.Interests
            .Where(interest => !interest.IsSuppressed)
            .GroupBy(interest => (interest.DeclaredExecutionId, interest.Identity))
            .Where(group => executionsById.GetValueOrDefault(group.Key.DeclaredExecutionId)?.HasRows == true)
            .OrderBy(group => querySequences.GetValueOrDefault(group.Key.DeclaredExecutionId))
            .ThenBy(group => group.Key.Identity.CanonicalValue, StringComparer.OrdinalIgnoreCase)
            .Select(group => new KustoRecordedTimelineValueViewModel(
                executionsById[group.Key.DeclaredExecutionId],
                valuesByIdentity[group.Key.Identity],
                group))
            .ToArray());
        selectedExecution = Executions.FirstOrDefault();
    }

    /// <summary>Gets the complete recorded session.</summary>
    public KustoRecordedSession Session { get; }

    /// <summary>Gets the session identifier.</summary>
    public Guid Id => Session.Summary.Id;

    /// <summary>Gets the session name.</summary>
    public string Name => Session.Summary.Name;

    /// <summary>Gets recorded executions in chronological order.</summary>
    public ReadOnlyCollection<KustoRecordedExecutionViewModel> Executions { get; }

    /// <summary>Gets the unique active values retained as pertinent to this session.</summary>
    public ReadOnlyCollection<KustoRecordedPertinentValueViewModel> PertinentValues { get; }

    /// <summary>Gets pertinent value declarations in chronological query order.</summary>
    public ReadOnlyCollection<KustoRecordedTimelineValueViewModel> TimelineValues { get; }

    /// <summary>Gets a value indicating whether this session contains pertinent values.</summary>
    public bool HasPertinentValues => PertinentValues.Count > 0;

    /// <summary>Gets a value indicating whether successful discoveries can be visualized.</summary>
    public bool HasTimelineValues => TimelineValues.Count > 0;

    /// <summary>Gets the pertinent-value count for display.</summary>
    public string PertinentValueCountText => $"{PertinentValues.Count:N0} values";

    /// <summary>Gets a concise timeline discovery count.</summary>
    public string TimelineCountText
    {
        get
        {
            int queryCount = TimelineValues.Select(value => value.ExecutionId).Distinct().Count();
            string discoveries = TimelineValues.Count == 1 ? "discovery" : "discoveries";
            string queries = queryCount == 1 ? "query" : "queries";
            return $"{TimelineValues.Count:N0} {discoveries} across {queryCount:N0} {queries}";
        }
    }

    /// <summary>Gets or sets the selected recorded execution.</summary>
    public KustoRecordedExecutionViewModel? SelectedExecution
    {
        get => selectedExecution;
        set => SetProperty(ref selectedExecution, value);
    }
}
