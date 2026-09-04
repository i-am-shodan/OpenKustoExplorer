using System.Text.Json;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Infrastructure.Storage;

namespace OpenKustoExplorer.Infrastructure.Dashboards;

/// <summary>
/// Persists KQL dashboards beneath local application data.
/// </summary>
public sealed class FileKustoDashboardStore : IKustoDashboardStore
{
    private readonly string filePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoDashboardStore"/> class using the default path.
    /// </summary>
    public FileKustoDashboardStore()
        : this(GetDefaultFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoDashboardStore"/> class.
    /// </summary>
    /// <param name="filePath">The absolute dashboard catalog path.</param>
    public FileKustoDashboardStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = Path.GetFullPath(filePath);
    }

    /// <inheritdoc />
    public KustoDashboardCatalog Load()
    {
        KustoDashboardCatalog catalog = new([]);

        if (File.Exists(filePath))
        {
            try
            {
                using FileStream stream = File.OpenRead(filePath);
                catalog = KustoDashboardCatalogJson.Read(stream);
            }
            catch (JsonException)
            {
                AtomicFileStore.PreserveUnreadable(filePath);
            }
            catch (InvalidDataException)
            {
                AtomicFileStore.PreserveUnreadable(filePath);
            }
            catch (ArgumentException)
            {
                AtomicFileStore.PreserveUnreadable(filePath);
            }
            catch (FormatException)
            {
                AtomicFileStore.PreserveUnreadable(filePath);
            }
        }

        return catalog;
    }

    /// <inheritdoc />
    public void Save(KustoDashboardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        AtomicFileStore.Write(filePath, stream => KustoDashboardCatalogJson.Write(stream, catalog));
    }

    /// <inheritdoc />
    public KustoDashboard Import(Stream stream)
    {
        KustoDashboardCatalog catalog = KustoDashboardCatalogJson.Read(stream);
        return catalog.Dashboards.Count == 1
            ? catalog.Dashboards[0]
            : throw new InvalidDataException("A dashboard import must contain exactly one dashboard.");
    }

    /// <inheritdoc />
    public void Export(Stream stream, KustoDashboard dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        KustoDashboardCatalogJson.Write(stream, new KustoDashboardCatalog([dashboard]));
    }

    private static string GetDefaultFilePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "OpenKustoExplorer", "dashboards.json");
    }
}
