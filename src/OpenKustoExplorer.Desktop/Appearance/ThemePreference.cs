namespace OpenKustoExplorer.Desktop.Appearance;

/// <summary>
/// Identifies how the desktop shell chooses its color theme.
/// </summary>
internal enum ThemePreference
{
    /// <summary>
    /// Follows the operating-system theme.
    /// </summary>
    System,

    /// <summary>
    /// Uses the light theme.
    /// </summary>
    Light,

    /// <summary>
    /// Uses the dark theme.
    /// </summary>
    Dark,
}
