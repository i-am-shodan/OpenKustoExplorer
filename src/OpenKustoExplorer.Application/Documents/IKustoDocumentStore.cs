namespace OpenKustoExplorer.Application.Documents;

/// <summary>
/// Persists the user's open KQL document tabs without authentication secrets.
/// </summary>
public interface IKustoDocumentStore
{
    /// <summary>
    /// Loads the persisted document workspace.
    /// </summary>
    /// <returns>The persisted workspace, or an empty workspace when no store exists.</returns>
    public KustoDocumentWorkspace Load();

    /// <summary>
    /// Atomically saves all open documents and the selected tab.
    /// </summary>
    /// <param name="workspace">The immutable workspace snapshot.</param>
    /// <param name="cancellationToken">Cancels waiting for durable storage.</param>
    /// <returns>A task that completes after the workspace is durable.</returns>
    public Task SaveAsync(
        KustoDocumentWorkspace workspace,
        CancellationToken cancellationToken = default);
}
