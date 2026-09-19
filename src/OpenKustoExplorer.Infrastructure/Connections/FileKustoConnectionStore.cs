using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Infrastructure.Storage;
using OpenKustoExplorer.Portable.Storage;

namespace OpenKustoExplorer.Infrastructure.Connections;

/// <summary>
/// Persists the connection catalog beneath the current user's local application-data directory.
/// </summary>
public sealed class FileKustoConnectionStore : IKustoConnectionStore
{
    private readonly string filePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoConnectionStore"/> class using the default application path.
    /// </summary>
    public FileKustoConnectionStore()
        : this(GetDefaultFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoConnectionStore"/> class.
    /// </summary>
    /// <param name="filePath">The absolute catalog file path.</param>
    public FileKustoConnectionStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = Path.GetFullPath(filePath);
    }

    /// <inheritdoc />
    public KustoConnectionCatalog Load()
    {
        return AtomicFileStore.ReadOrDefault(
            filePath,
            KustoConnectionCatalogJson.Read,
            static () => new KustoConnectionCatalog([]));
    }

    /// <inheritdoc />
    public Task SaveAsync(
        KustoConnectionCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        cancellationToken.ThrowIfCancellationRequested();
        AtomicFileStore.Write(filePath, stream => KustoConnectionCatalogJson.Write(stream, catalog));
        return Task.CompletedTask;
    }

    private static string GetDefaultFilePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string defaultFilePath = Path.Join(localApplicationData, "OpenKustoExplorer", "connections.json");
        return defaultFilePath;
    }
}
