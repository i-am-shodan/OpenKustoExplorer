using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Resolves one possible duplicate graph identity before staged rows are committed.
/// </summary>
/// <param name="conflict">The candidate identities and suggested merge target.</param>
/// <param name="cancellationToken">A token that cancels the pending decision.</param>
/// <returns>The analyst's resolution.</returns>
public delegate Task<GraphIdentityResolutionDecision> GraphIdentityConflictResolver(
    GraphEntityIdentityConflict conflict,
    CancellationToken cancellationToken);
