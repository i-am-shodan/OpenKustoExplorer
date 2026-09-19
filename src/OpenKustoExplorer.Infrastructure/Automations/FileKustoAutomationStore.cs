using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Infrastructure.Storage;
using OpenKustoExplorer.Portable.Storage;

namespace OpenKustoExplorer.Infrastructure.Automations;

/// <summary>
/// Persists scheduled queries and bounded result history beneath local application data.
/// </summary>
public sealed class FileKustoAutomationStore : IKustoAutomationStore
{
    private readonly string filePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoAutomationStore"/> class using the default path.
    /// </summary>
    public FileKustoAutomationStore()
        : this(GetDefaultFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoAutomationStore"/> class.
    /// </summary>
    /// <param name="filePath">The absolute automation catalog path.</param>
    public FileKustoAutomationStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = Path.GetFullPath(filePath);
    }

    /// <inheritdoc />
    public KustoAutomationCatalog Load()
    {
        return AtomicFileStore.ReadOrDefault(
            filePath,
            KustoAutomationCatalogJson.Read,
            static () => new KustoAutomationCatalog([]));
    }

    /// <inheritdoc />
    public Task SaveAsync(
        KustoAutomationCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        cancellationToken.ThrowIfCancellationRequested();
        AtomicFileStore.Write(filePath, stream => KustoAutomationCatalogJson.Write(stream, catalog));
        return Task.CompletedTask;
    }

    private static string GetDefaultFilePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "OpenKustoExplorer", "automations.json");
    }
}
