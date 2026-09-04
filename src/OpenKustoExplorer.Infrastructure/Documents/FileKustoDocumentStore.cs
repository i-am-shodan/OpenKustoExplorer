using System.Text.Json;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Infrastructure.Storage;

namespace OpenKustoExplorer.Infrastructure.Documents;

/// <summary>
/// Persists open KQL document tabs beneath the current user's local application-data directory.
/// </summary>
public sealed class FileKustoDocumentStore : IKustoDocumentStore
{
    private readonly string filePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoDocumentStore"/> class using the default application path.
    /// </summary>
    public FileKustoDocumentStore()
        : this(GetDefaultFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoDocumentStore"/> class.
    /// </summary>
    /// <param name="filePath">The absolute workspace file path.</param>
    public FileKustoDocumentStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = Path.GetFullPath(filePath);
    }

    /// <inheritdoc />
    public KustoDocumentWorkspace Load()
    {
        KustoDocumentWorkspace workspace = new([], null);

        if (File.Exists(filePath))
        {
            try
            {
                using FileStream stream = File.OpenRead(filePath);
                workspace = KustoDocumentWorkspaceJson.Read(stream);
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

        return workspace;
    }

    /// <inheritdoc />
    public void Save(KustoDocumentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        AtomicFileStore.Write(filePath, stream => KustoDocumentWorkspaceJson.Write(stream, workspace));
    }

    private static string GetDefaultFilePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "OpenKustoExplorer", "documents.json");
    }
}
