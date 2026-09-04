namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Starts a user-configured application for a matched automation action.
/// </summary>
internal interface IAutomationApplicationLauncher
{
    /// <summary>
    /// Starts an application without waiting for it to exit.
    /// </summary>
    /// <param name="applicationPath">The executable path.</param>
    /// <param name="arguments">The optional command-line arguments.</param>
    public void Launch(string applicationPath, string? arguments);
}
