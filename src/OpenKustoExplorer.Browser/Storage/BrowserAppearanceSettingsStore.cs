using System.Text.Json;
using OpenKustoExplorer.Desktop.Appearance;

namespace OpenKustoExplorer.Browser.Storage;

/// <summary>
/// Persists one account's shared appearance settings in browser local storage.
/// </summary>
internal sealed class BrowserAppearanceSettingsStore : IAppearanceSettingsStore
{
    private readonly string partition;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserAppearanceSettingsStore"/> class.
    /// </summary>
    /// <param name="partition">The authenticated browser-storage partition.</param>
    internal BrowserAppearanceSettingsStore(string partition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        this.partition = partition;
    }

    /// <inheritdoc />
    public string? Load()
    {
        string settingsJson = BrowserStorageInterop.ReadSetting(partition);
        if (string.IsNullOrEmpty(settingsJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(settingsJson);
            bool isSupported = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("version", out JsonElement version)
                && version.TryGetInt32(out int versionNumber)
                && versionNumber == 1;
            if (isSupported)
            {
                return settingsJson;
            }
        }
        catch (JsonException)
        {
            // Invalid settings are archived below before accessible defaults are used.
        }

        BrowserStorageInterop.PreserveInvalidSetting(partition, settingsJson);
        BrowserInterop.ShowToast(
            "Browser storage recovered",
            "Application settings were archived and reset because they could not be read.");
        return null;
    }

    /// <inheritdoc />
    public void Save(string settingsJson)
    {
        ArgumentNullException.ThrowIfNull(settingsJson);
        BrowserStorageInterop.WriteSetting(partition, settingsJson);
    }
}
