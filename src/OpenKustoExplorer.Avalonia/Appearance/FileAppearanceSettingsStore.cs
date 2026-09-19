using System.Text;

namespace OpenKustoExplorer.Desktop.Appearance;

/// <summary>
/// Persists appearance settings atomically on the local filesystem.
/// </summary>
internal sealed class FileAppearanceSettingsStore : IAppearanceSettingsStore
{
    private readonly string filePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileAppearanceSettingsStore"/> class.
    /// </summary>
    /// <param name="filePath">The absolute settings file path.</param>
    internal FileAppearanceSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = Path.GetFullPath(filePath);
    }

    /// <inheritdoc />
    public string? Load() => File.Exists(filePath) ? File.ReadAllText(filePath) : null;

    /// <inheritdoc />
    public void Save(string settingsJson)
    {
        ArgumentNullException.ThrowIfNull(settingsJson);
        string directoryPath = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("The application settings path has no directory.");
        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(directoryPath);
            byte[] bytes = Encoding.UTF8.GetBytes(settingsJson);
            using (FileStream stream = File.Create(temporaryPath))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }

            File.Move(temporaryPath, filePath, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
