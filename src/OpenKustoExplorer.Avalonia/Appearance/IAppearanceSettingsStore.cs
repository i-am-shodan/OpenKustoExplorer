namespace OpenKustoExplorer.Desktop.Appearance;

/// <summary>
/// Persists the shared appearance-settings JSON for one host.
/// </summary>
public interface IAppearanceSettingsStore
{
    /// <summary>
    /// Loads the persisted settings JSON, or <see langword="null"/> when none exists.
    /// </summary>
    /// <returns>The persisted settings JSON.</returns>
    public string? Load();

    /// <summary>
    /// Saves the complete settings JSON.
    /// </summary>
    /// <param name="settingsJson">The versioned settings JSON.</param>
    public void Save(string settingsJson);
}
