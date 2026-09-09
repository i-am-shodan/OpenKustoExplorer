namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Converts weighted pivot evidence into a minimized source-relation plan.
/// </summary>
public interface IKustoRecordedRelationPlanner
{
    /// <summary>
    /// Creates a relational plan when every retained pivot has sufficient lineage evidence.
    /// </summary>
    /// <param name="session">The recorded session.</param>
    /// <param name="chain">The selected weighted pivot chain.</param>
    /// <returns>The minimized plan, or <see langword="null"/> when generation is unsafe.</returns>
    public KustoRelationalChainPlan? CreatePlan(KustoRecordedSession session, KustoQueryChain chain);
}
