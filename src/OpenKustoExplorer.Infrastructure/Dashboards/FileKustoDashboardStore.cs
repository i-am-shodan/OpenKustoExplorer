using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Infrastructure.Storage;
using OpenKustoExplorer.Portable.Storage;

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
        return AtomicFileStore.ReadOrDefault(
            filePath,
            KustoDashboardCatalogJson.Read,
            static () => new KustoDashboardCatalog([]));
    }

    /// <summary>
    /// Saves the dashboard catalog synchronously.
    /// </summary>
    /// <param name="catalog">The dashboard catalog.</param>
    public void Save(KustoDashboardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        AtomicFileStore.Write(filePath, stream => KustoDashboardCatalogJson.Write(stream, catalog));
    }

    /// <inheritdoc />
    public Task SaveAsync(
        KustoDashboardCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(catalog);
        return Task.CompletedTask;
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
