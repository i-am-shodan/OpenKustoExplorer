namespace OpenKustoExplorer.Application.Connections;

/// <summary>
/// Persists the user's Azure Data Explorer connection catalog without authentication secrets.
/// </summary>
public interface IKustoConnectionStore
{
    /// <summary>
    /// Loads the persisted cluster, database, and cached schema catalog.
    /// </summary>
    /// <returns>The persisted catalog, or an empty catalog when no store exists.</returns>
    public KustoConnectionCatalog Load();

    /// <summary>
    /// Atomically saves the cluster, database, and cached schema catalog.
    /// </summary>
    /// <param name="catalog">The immutable catalog to save.</param>
    public void Save(KustoConnectionCatalog catalog);
}
