using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one pertinent value declaration on the recorded-session timeline.
/// </summary>
public sealed class KustoRecordedTimelineValueViewModel
{
    private readonly KustoRecordedPertinentValueViewModel pertinentValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedTimelineValueViewModel"/> class.
    /// </summary>
    /// <param name="execution">The query that declared the value.</param>
    /// <param name="pertinentValue">The session-wide pertinent value.</param>
    /// <param name="declarations">Declarations made by the query.</param>
    public KustoRecordedTimelineValueViewModel(
        KustoRecordedExecutionViewModel execution,
        KustoRecordedPertinentValueViewModel pertinentValue,
        IEnumerable<KustoRecordedInterest> declarations)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(pertinentValue);
        ArgumentNullException.ThrowIfNull(declarations);
        KustoRecordedInterest[] materializedDeclarations = declarations.ToArray();
        if (materializedDeclarations.Length == 0)
        {
            throw new ArgumentException("At least one timeline declaration is required.", nameof(declarations));
        }

        this.pertinentValue = pertinentValue;
        ExecutionId = execution.Id;
        QueryTitle = execution.QueryTitle;
        FlowText = execution.DiscoveryFlowText;
        ColumnsText = string.Join(", ", materializedDeclarations
            .Select(declaration => declaration.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase));
        IsAddedByUser = materializedDeclarations.Any(
            declaration => declaration.Source == KustoRecordedInterestSource.ManualCell);
        IsExtracted = materializedDeclarations.Any(
            declaration => declaration.Source is KustoRecordedInterestSource.QueryPredicate
                or KustoRecordedInterestSource.ConfirmedPredicate);
        SourceText = (IsAddedByUser, IsExtracted) switch
        {
            (true, true) => "Added + extracted automatically",
            (true, false) => "Added by you",
            _ => "Extracted automatically",
        };
        QueryCoordinate = pertinentValue.Coordinates.FirstOrDefault(
            coordinate => coordinate.ExecutionId == execution.Id);
        AutomationName = $"{QueryTitle}, {pertinentValue.ValueText}, {SourceText}, {ColumnsText}";
    }

    /// <summary>Gets the query identifier.</summary>
    public Guid ExecutionId { get; }

    /// <summary>Gets the query title.</summary>
    public string QueryTitle { get; }

    /// <summary>Gets the pertinent value display text.</summary>
    public string ValueText => pertinentValue.ValueText;

    /// <summary>Gets the normalized value type.</summary>
    public string TypeName => pertinentValue.TypeName;

    /// <summary>Gets the stable accent color assigned to this value.</summary>
    public string AccentColorHex => pertinentValue.AccentColorHex;

    /// <summary>Gets the translucent highlight color assigned to this value.</summary>
    public string HighlightColorHex => pertinentValue.HighlightColorHex;

    /// <summary>Gets the declaring result or predicate columns.</summary>
    public string ColumnsText { get; }

    /// <summary>Gets the query's pertinent input-to-output column flow.</summary>
    public string FlowText { get; }

    /// <summary>Gets how the value was declared by this query.</summary>
    public string SourceText { get; }

    /// <summary>Gets a value indicating whether this query contains a manual declaration.</summary>
    public bool IsAddedByUser { get; }

    /// <summary>Gets a value indicating whether this query contains an extracted declaration.</summary>
    public bool IsExtracted { get; }

    /// <summary>Gets a value indicating whether this value can become an endpoint.</summary>
    public bool HasCoordinates => pertinentValue.HasCoordinates;

    /// <summary>Gets a value indicating whether this value is the chain start.</summary>
    public bool IsChainStart => pertinentValue.IsChainStart;

    /// <summary>Gets a value indicating whether this value is the chain end.</summary>
    public bool IsChainEnd => pertinentValue.IsChainEnd;

    /// <summary>Gets an accessible timeline-node description.</summary>
    public string AutomationName { get; }

    /// <summary>Gets the optional result coordinate declared by this query.</summary>
    internal KustoRecordedValueCoordinate? QueryCoordinate { get; }

    /// <summary>Gets the session-wide pertinent value.</summary>
    internal KustoRecordedPertinentValueViewModel PertinentValue => pertinentValue;
}
