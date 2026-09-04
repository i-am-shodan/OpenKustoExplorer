namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Persists scheduled query definitions and bounded result history without credentials.
/// </summary>
public interface IKustoAutomationStore
{
    /// <summary>
    /// Loads the persisted automation catalog.
    /// </summary>
    /// <returns>The persisted catalog or an empty catalog.</returns>
    public KustoAutomationCatalog Load();

    /// <summary>
    /// Atomically saves all automation definitions and run histories.
    /// </summary>
    /// <param name="catalog">The immutable automation catalog.</param>
    public void Save(KustoAutomationCatalog catalog);
}
