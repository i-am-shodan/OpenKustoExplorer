namespace OpenKustoExplorer.Portable.Graphs;

/// <summary>
/// Loads and saves one complete portable graph snapshot.
/// </summary>
public interface IGraphSnapshotStore
{
    /// <summary>
    /// Loads the persisted snapshot JSON, or <see langword="null"/> when none exists.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The persisted snapshot JSON.</returns>
    public Task<string?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the complete versioned snapshot JSON.
    /// </summary>
    /// <param name="snapshotJson">The complete snapshot JSON.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the snapshot is durable.</returns>
    public Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default);
}
