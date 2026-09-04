namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Indicates that an analyst canceled graph import while resolving ambiguous node identities.
/// </summary>
public sealed class GraphIdentityResolutionCanceledException : OperationCanceledException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphIdentityResolutionCanceledException"/> class.
    /// </summary>
    public GraphIdentityResolutionCanceledException()
        : base("Graph import was canceled while resolving duplicate nodes.")
    {
    }
}
