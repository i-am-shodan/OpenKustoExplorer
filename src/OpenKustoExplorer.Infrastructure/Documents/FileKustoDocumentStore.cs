using System.Text.Json;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Infrastructure.Storage;
using OpenKustoExplorer.Portable.Storage;

namespace OpenKustoExplorer.Infrastructure.Documents;

/// <summary>
/// Persists open KQL document tabs beneath the current user's local application-data directory.
/// </summary>
public sealed class FileKustoDocumentStore : IKustoDocumentStore
{
    private readonly string filePath;
    private readonly string recoveryFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoDocumentStore"/> class using the default application path.
    /// </summary>
    public FileKustoDocumentStore()
        : this(GetDefaultFilePath(), GetDefaultRecoveryFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoDocumentStore"/> class.
    /// </summary>
    /// <param name="filePath">The absolute workspace file path.</param>
    public FileKustoDocumentStore(string filePath)
        : this(filePath, GetDefaultRecoveryFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileKustoDocumentStore"/> class with a recovery path.
    /// </summary>
    /// <param name="filePath">The absolute workspace file path.</param>
    /// <param name="recoveryFilePath">The absolute fallback recovery path.</param>
    internal FileKustoDocumentStore(string filePath, string recoveryFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryFilePath);
        this.filePath = Path.GetFullPath(filePath);
        this.recoveryFilePath = Path.GetFullPath(recoveryFilePath);
    }

    /// <inheritdoc />
    public KustoDocumentWorkspace Load()
    {
        return AtomicFileStore.ReadOrDefault(
            filePath,
            KustoDocumentWorkspaceJson.Read,
            static () => new KustoDocumentWorkspace([], null));
    }

    /// <summary>
    /// Saves the document workspace synchronously.
    /// </summary>
    /// <param name="workspace">The document workspace.</param>
    public void Save(KustoDocumentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        try
        {
            AtomicFileStore.Write(filePath, stream => KustoDocumentWorkspaceJson.Write(stream, workspace));
            TryDeleteRecoveryFile();
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            string message = TryWriteRecoveryFile(workspace)
                ? $"A recovery copy was saved to '{recoveryFilePath}'."
                : "The recovery copy could not be written either.";
            throw new IOException(message, exception);
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(
        KustoDocumentWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(workspace);
        return Task.CompletedTask;
    }

    private static string GetDefaultFilePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "OpenKustoExplorer", "documents.json");
    }

    private static string GetDefaultRecoveryFilePath()
    {
        return Path.Combine(Path.GetTempPath(), "OpenKustoExplorer", "documents-recovery.json");
    }

    private static bool IsPersistenceFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException;
    }

    private bool TryWriteRecoveryFile(KustoDocumentWorkspace workspace)
    {
        try
        {
            AtomicFileStore.Write(
                recoveryFilePath,
                stream => KustoDocumentWorkspaceJson.Write(stream, workspace));
            return true;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            return false;
        }
    }

    private void TryDeleteRecoveryFile()
    {
        try
        {
            File.Delete(recoveryFilePath);
        }
        catch (IOException)
        {
            // A stale recovery copy is preferable to failing a successful primary save.
        }
        catch (UnauthorizedAccessException)
        {
            // A stale recovery copy is preferable to failing a successful primary save.
        }
    }
}
