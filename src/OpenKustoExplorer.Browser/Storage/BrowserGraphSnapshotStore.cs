using OpenKustoExplorer.Portable.Graphs;

namespace OpenKustoExplorer.Browser.Storage;

/// <summary>
/// Persists the portable graph snapshot in partitioned IndexedDB storage.
/// </summary>
internal sealed class BrowserGraphSnapshotStore : IGraphSnapshotStore
{
    private const string StoreName = "graphs";
    private readonly string partition;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserGraphSnapshotStore"/> class.
    /// </summary>
    /// <param name="partition">The authenticated browser-storage partition.</param>
    internal BrowserGraphSnapshotStore(string partition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        this.partition = partition;
    }

    /// <inheritdoc />
    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string json = await BrowserStorageInterop.ReadAsync(partition, StoreName).ConfigureAwait(false);
        return string.IsNullOrEmpty(json) ? null : json;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotJson);
        cancellationToken.ThrowIfCancellationRequested();
        await BrowserStorageInterop.WriteAsync(partition, StoreName, snapshotJson).ConfigureAwait(false);
    }
}
