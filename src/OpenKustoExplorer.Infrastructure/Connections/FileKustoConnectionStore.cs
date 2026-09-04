using System.Text.Json;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Infrastructure.Storage;

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
        KustoConnectionCatalog catalog = new([]);

        if (File.Exists(filePath))
        {
            try
            {
                using FileStream stream = File.OpenRead(filePath);
                catalog = KustoConnectionCatalogJson.Read(stream);
            }
            catch (JsonException)
            {
                AtomicFileStore.PreserveUnreadable(filePath);
            }
            catch (InvalidDataException)
            {
                AtomicFileStore.PreserveUnreadable(filePath);
            }
        }

        return catalog;
    }

    /// <inheritdoc />
    public void Save(KustoConnectionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        AtomicFileStore.Write(filePath, stream => KustoConnectionCatalogJson.Write(stream, catalog));
    }

    private static string GetDefaultFilePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string filePath = Path.Combine(localApplicationData, "OpenKustoExplorer", "connections.json");
        return filePath;
    }
}
