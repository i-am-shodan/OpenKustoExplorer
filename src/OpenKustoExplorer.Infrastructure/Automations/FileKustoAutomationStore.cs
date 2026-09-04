using System.Text.Json;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Infrastructure.Storage;

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
        KustoAutomationCatalog catalog = new([]);

        if (File.Exists(filePath))
        {
            try
            {
                using FileStream stream = File.OpenRead(filePath);
                catalog = KustoAutomationCatalogJson.Read(stream);
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
    public void Save(KustoAutomationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        AtomicFileStore.Write(filePath, stream => KustoAutomationCatalogJson.Write(stream, catalog));
    }

    private static string GetDefaultFilePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "OpenKustoExplorer", "automations.json");
    }
}
