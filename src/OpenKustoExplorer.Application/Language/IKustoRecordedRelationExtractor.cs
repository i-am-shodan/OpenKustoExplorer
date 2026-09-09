using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Extracts conservative source-relation lineage from recorded KQL.
/// </summary>
public interface IKustoRecordedRelationExtractor
{
    /// <summary>
    /// Extracts a source relation when direct result-column lineage can be proven.
    /// </summary>
    /// <param name="queryText">The executable KQL query.</param>
    /// <param name="databaseSchema">The active database schema.</param>
    /// <param name="cancellationToken">Cancels parsing and binding.</param>
    /// <returns>The relation descriptor, or <see langword="null"/> for unsupported queries.</returns>
    public KustoRecordedRelationDescriptor? Extract(
        string queryText,
        KustoDatabaseSchema databaseSchema,
        CancellationToken cancellationToken = default);
}
