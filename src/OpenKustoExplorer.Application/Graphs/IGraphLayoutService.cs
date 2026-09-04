using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Graphs;

/// <summary>
/// Calculates render geometry for a bounded graph viewport.
/// </summary>
public interface IGraphLayoutService
{
    /// <summary>
    /// Calculates node positions and edge routes without blocking the caller's UI thread.
    /// </summary>
    /// <param name="viewport">The bounded graph viewport.</param>
    /// <param name="cancellationToken">A token that cancels layout.</param>
    /// <returns>The normalized render geometry.</returns>
    public Task<GraphLayout> LayoutAsync(
        GraphViewport viewport,
        CancellationToken cancellationToken = default);
}
