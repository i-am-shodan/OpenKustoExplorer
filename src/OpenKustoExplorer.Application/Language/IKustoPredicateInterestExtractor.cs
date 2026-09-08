using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Extracts stable values of interest from exact KQL filter predicates.
/// </summary>
public interface IKustoPredicateInterestExtractor
{
    /// <summary>
    /// Extracts exact, schema-bound predicate literals from one query.
    /// </summary>
    /// <param name="queryText">The complete executable KQL query.</param>
    /// <param name="databaseSchema">The active database schema.</param>
    /// <param name="cancellationToken">Cancels parsing and binding.</param>
    /// <returns>Eligible predicate interests in source order.</returns>
    public IReadOnlyList<KustoPredicateInterest> Extract(
        string queryText,
        KustoDatabaseSchema databaseSchema,
        CancellationToken cancellationToken = default);
}
