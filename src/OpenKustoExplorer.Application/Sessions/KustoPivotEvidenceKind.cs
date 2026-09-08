namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Identifies the strength and provenance of an inferred same-row pivot.
/// </summary>
public enum KustoPivotEvidenceKind
{
    /// <summary>A same-query predicate input co-occurs with a marked or predicate-confirmed output.</summary>
    PredicateToManual = 1,

    /// <summary>A previously interesting input co-occurs with a marked or predicate-confirmed output.</summary>
    PriorInterestToManual = 2,

    /// <summary>An input or output became explicit only in a later execution.</summary>
    RetrospectiveToManual = 4,

    /// <summary>The values share a row without stronger analyst evidence.</summary>
    GenericCorrelation = 8,

    /// <summary>An exact predicate input produced an output used by another exact predicate.</summary>
    PredicateToPredicate = 16,
}
