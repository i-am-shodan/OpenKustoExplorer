using OpenKustoExplorer.Application.Sessions;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one unique value retained as pertinent to a recorded session.
/// </summary>
public sealed class KustoRecordedPertinentValueViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedPertinentValueViewModel"/> class.
    /// </summary>
    /// <param name="interests">The active declarations for one typed value.</param>
    /// <param name="queryTitles">Recorded query titles keyed by execution identifier.</param>
    /// <param name="endpoints">The selected chain endpoints.</param>
    public KustoRecordedPertinentValueViewModel(
        IEnumerable<KustoRecordedInterest> interests,
        IReadOnlyDictionary<Guid, string> queryTitles,
        IReadOnlyList<KustoChainEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(interests);
        ArgumentNullException.ThrowIfNull(queryTitles);
        ArgumentNullException.ThrowIfNull(endpoints);
        KustoRecordedInterest[] declarations = interests.ToArray();
        if (declarations.Length == 0)
        {
            throw new ArgumentException("At least one interest is required.", nameof(interests));
        }

        KustoRecordedValueIdentity identity = declarations[0].Identity;
        Identity = identity;
        KustoRecordedValueColor color = KustoRecordedValueColorPalette.GetColor(identity);
        AccentColorHex = color.AccentHex;
        HighlightColorHex = color.HighlightHex;
        ValueText = identity.CanonicalValue.Length == 0 ? "(empty)" : identity.CanonicalValue;
        if (identity.IsNull)
        {
            ValueText = "(null)";
        }

        TypeName = identity.TypeName;
        Coordinates = declarations
            .Where(interest => interest.Coordinate is not null)
            .Select(interest => interest.Coordinate!)
            .DistinctBy(coordinate => (
                coordinate.ExecutionId,
                coordinate.TableOrdinal,
                coordinate.RowOrdinal,
                coordinate.ColumnOrdinal))
            .ToArray();
        IsAddedByUser = declarations.Any(
            interest => interest.Source == KustoRecordedInterestSource.ManualCell);
        IsExtracted = declarations.Any(
            interest => interest.Source is KustoRecordedInterestSource.QueryPredicate
                or KustoRecordedInterestSource.ConfirmedPredicate);
        SourceText = (IsAddedByUser, IsExtracted) switch
        {
            (true, true) => "Added + extracted",
            (true, false) => "Added by you",
            _ => "Extracted",
        };
        ColumnsText = string.Join(", ", declarations
            .Select(interest => interest.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase));
        QueriesText = string.Join(", ", declarations
            .Select(interest => queryTitles.GetValueOrDefault(interest.DeclaredExecutionId))
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        IsChainStart = HasEndpoint(endpoints, KustoChainEndpointRole.Start);
        IsChainEnd = HasEndpoint(endpoints, KustoChainEndpointRole.End);
        ContextText = QueriesText.Length == 0
            ? $"{ColumnsText} · {TypeName}"
            : $"{QueriesText} · {ColumnsText} · {TypeName}";
        AutomationName = $"{ValueText}, {SourceText}, {ContextText}";
    }

    /// <summary>Gets the display value.</summary>
    public string ValueText { get; }

    /// <summary>Gets the normalized Kusto type.</summary>
    public string TypeName { get; }

    /// <summary>Gets the stable accent color assigned to this value.</summary>
    public string AccentColorHex { get; }

    /// <summary>Gets the translucent highlight color assigned to this value.</summary>
    public string HighlightColorHex { get; }

    /// <summary>Gets the columns associated with this value.</summary>
    public string ColumnsText { get; }

    /// <summary>Gets the queries associated with this value.</summary>
    public string QueriesText { get; }

    /// <summary>Gets concrete result occurrences in chronological order.</summary>
    public IReadOnlyList<KustoRecordedValueCoordinate> Coordinates { get; }

    /// <summary>Gets a value indicating whether this value can be used as a chain endpoint.</summary>
    public bool HasCoordinates => Coordinates.Count > 0;

    /// <summary>Gets a value indicating whether this value is the chain start.</summary>
    public bool IsChainStart { get; }

    /// <summary>Gets a value indicating whether this value is the chain end.</summary>
    public bool IsChainEnd { get; }

    /// <summary>Gets the selected endpoint role text.</summary>
    public string EndpointText => (IsChainStart, IsChainEnd) switch
    {
        (true, true) => "Start + End",
        (true, false) => "Start",
        (false, true) => "End",
        _ => string.Empty,
    };

    /// <summary>Gets a value indicating whether the user explicitly added this value.</summary>
    public bool IsAddedByUser { get; }

    /// <summary>Gets a value indicating whether this value was extracted from a query predicate.</summary>
    public bool IsExtracted { get; }

    /// <summary>Gets a concise description of how the value became pertinent.</summary>
    public string SourceText { get; }

    /// <summary>Gets the queries, columns, and type associated with the value.</summary>
    public string ContextText { get; }

    /// <summary>Gets an accessible value summary.</summary>
    public string AutomationName { get; }

    /// <summary>Gets the exact typed value identity.</summary>
    internal KustoRecordedValueIdentity Identity { get; }

    private static bool CoordinatesEqual(
        KustoRecordedValueCoordinate left,
        KustoRecordedValueCoordinate right)
    {
        return left.ExecutionId == right.ExecutionId
            && left.TableOrdinal == right.TableOrdinal
            && left.RowOrdinal == right.RowOrdinal
            && left.ColumnOrdinal == right.ColumnOrdinal;
    }

    private bool HasEndpoint(
        IReadOnlyList<KustoChainEndpoint> endpoints,
        KustoChainEndpointRole role)
    {
        KustoRecordedValueCoordinate? endpoint = endpoints.FirstOrDefault(
            candidate => candidate.Role == role)?.Coordinate;
        return endpoint is not null && Coordinates.Any(coordinate => CoordinatesEqual(coordinate, endpoint));
    }
}
