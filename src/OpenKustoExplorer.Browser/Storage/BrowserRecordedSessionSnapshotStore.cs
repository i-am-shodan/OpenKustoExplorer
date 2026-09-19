using OpenKustoExplorer.Portable.Sessions;

namespace OpenKustoExplorer.Browser.Storage;

/// <summary>
/// Persists the portable recorded-session snapshot in partitioned IndexedDB storage.
/// </summary>
internal sealed class BrowserRecordedSessionSnapshotStore : IKustoRecordedSessionSnapshotStore
{
    private const string StoreName = "recorded-sessions";
    private readonly string partition;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserRecordedSessionSnapshotStore"/> class.
    /// </summary>
    /// <param name="partition">The authenticated browser-storage partition.</param>
    internal BrowserRecordedSessionSnapshotStore(string partition)
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
