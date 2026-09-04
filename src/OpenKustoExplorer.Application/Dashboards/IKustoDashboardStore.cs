namespace OpenKustoExplorer.Application.Dashboards;

/// <summary>
/// Loads, saves, imports, and exports KQL dashboards.
/// </summary>
public interface IKustoDashboardStore
{
    /// <summary>
    /// Loads the persisted dashboard catalog.
    /// </summary>
    /// <returns>The persisted catalog.</returns>
    public KustoDashboardCatalog Load();

    /// <summary>
    /// Atomically saves the dashboard catalog.
    /// </summary>
    /// <param name="catalog">The complete catalog.</param>
    public void Save(KustoDashboardCatalog catalog);

    /// <summary>
    /// Imports one dashboard from JSON.
    /// </summary>
    /// <param name="stream">The readable JSON stream.</param>
    /// <returns>The imported dashboard.</returns>
    public KustoDashboard Import(Stream stream);

    /// <summary>
    /// Exports one dashboard as JSON.
    /// </summary>
    /// <param name="stream">The writable JSON stream.</param>
    /// <param name="dashboard">The dashboard to export.</param>
    public void Export(Stream stream, KustoDashboard dashboard);
}
