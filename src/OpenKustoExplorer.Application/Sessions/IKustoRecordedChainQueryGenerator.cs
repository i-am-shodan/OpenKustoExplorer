using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Generates and validates KQL for a minimized recorded pivot plan.
/// </summary>
public interface IKustoRecordedChainQueryGenerator
{
    /// <summary>
    /// Generates one runnable query with a typed top-level input variable.
    /// </summary>
    /// <param name="plan">The minimized relational plan.</param>
    /// <param name="databaseSchema">The current target database schema.</param>
    /// <returns>Generated KQL or validation diagnostics.</returns>
    public KustoGeneratedChainQuery Generate(
        KustoRelationalChainPlan plan,
        KustoDatabaseSchema databaseSchema);
}
