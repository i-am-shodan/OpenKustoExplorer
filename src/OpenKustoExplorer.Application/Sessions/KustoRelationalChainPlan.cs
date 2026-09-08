namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Contains the minimized source-relation plan for a recorded pivot chain.
/// </summary>
public sealed class KustoRelationalChainPlan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRelationalChainPlan"/> class.
    /// </summary>
    /// <param name="sessionId">The owning session identifier.</param>
    /// <param name="clusterUri">The common target cluster.</param>
    /// <param name="databaseName">The common target database.</param>
    /// <param name="steps">The retained source relations in traversal order.</param>
    public KustoRelationalChainPlan(
        Guid sessionId,
        Uri clusterUri,
        string databaseName,
        IEnumerable<KustoRelationalChainStep> steps)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(steps);
        KustoRelationalChainStep[] materializedSteps = steps.ToArray();
        if (materializedSteps.Length == 0)
        {
            throw new ArgumentException("A relational chain plan requires at least one step.", nameof(steps));
        }

        SessionId = sessionId;
        ClusterUri = clusterUri;
        DatabaseName = databaseName.Trim();
        Steps = Array.AsReadOnly(materializedSteps);
    }

    /// <summary>Gets the owning session identifier.</summary>
    public Guid SessionId { get; }

    /// <summary>Gets the common target cluster.</summary>
    public Uri ClusterUri { get; }

    /// <summary>Gets the common target database.</summary>
    public string DatabaseName { get; }

    /// <summary>Gets retained source relations in traversal order.</summary>
    public IReadOnlyList<KustoRelationalChainStep> Steps { get; }
}
