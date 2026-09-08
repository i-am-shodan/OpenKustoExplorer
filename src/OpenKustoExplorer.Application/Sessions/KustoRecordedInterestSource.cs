namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Identifies how a recorded value became interesting.
/// </summary>
public enum KustoRecordedInterestSource
{
    /// <summary>The value appeared in an exact query predicate.</summary>
    QueryPredicate,

    /// <summary>The user explicitly marked a result cell.</summary>
    ManualCell,

    /// <summary>An exact query predicate was confirmed by a returned result cell.</summary>
    ConfirmedPredicate,
}
