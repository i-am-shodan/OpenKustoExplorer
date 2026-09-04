namespace OpenKustoExplorer.Infrastructure.Storage;

/// <summary>
/// Writes files atomically and preserves unreadable files so persisted data is never silently lost.
/// </summary>
internal static class AtomicFileStore
{
    /// <summary>
    /// Atomically writes a file by staging content in a temporary file and moving it into place.
    /// </summary>
    /// <param name="filePath">The absolute destination path.</param>
    /// <param name="writeContent">Writes the complete file content to the supplied stream.</param>
    internal static void Write(string filePath, Action<Stream> writeContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(writeContent);

        string directoryPath = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException($"The path '{filePath}' has no directory.");
        Directory.CreateDirectory(directoryPath);
        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            using (FileStream stream = File.Create(temporaryPath))
            {
                writeContent(stream);
                stream.Flush(true);
            }

            File.Move(temporaryPath, filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// Moves an unreadable file to a timestamped backup so a later write cannot overwrite recoverable data.
    /// </summary>
    /// <param name="filePath">The absolute path of the file that failed to parse.</param>
    internal static void PreserveUnreadable(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            if (File.Exists(filePath))
            {
                string backupPath = $"{filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak";
                File.Move(filePath, backupPath, true);
            }
        }
        catch (IOException)
        {
            // Preserving a locked or unreadable file is best effort and must not block startup.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserving the file is optional when the location is not writable.
        }
    }
}
