using System.Text.Json;

namespace OpenKustoExplorer.Infrastructure.Storage;

/// <summary>
/// Writes files atomically and preserves unreadable files so persisted data is never silently lost.
/// </summary>
internal static class AtomicFileStore
{
    /// <summary>
    /// Reads a persisted value, preserving malformed input and returning a clean default.
    /// </summary>
    /// <typeparam name="T">The persisted value type.</typeparam>
    /// <param name="filePath">The absolute source path.</param>
    /// <param name="readContent">Reads the complete value from the supplied stream.</param>
    /// <param name="createDefault">Creates the value returned when no readable file exists.</param>
    /// <returns>The persisted value, or a clean default when the file is absent or malformed.</returns>
    internal static T ReadOrDefault<T>(
        string filePath,
        Func<Stream, T> readContent,
        Func<T> createDefault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(readContent);
        ArgumentNullException.ThrowIfNull(createDefault);

        if (!File.Exists(filePath))
        {
            return createDefault();
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            return readContent(stream);
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidDataException
            or ArgumentException
            or FormatException)
        {
            PreserveUnreadable(filePath);
            return createDefault();
        }
    }

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
