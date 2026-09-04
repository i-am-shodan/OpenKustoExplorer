using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Application.Connections;

/// <summary>
/// Discovers accessible databases and schemas from an Azure Data Explorer cluster.
/// </summary>
public interface IKustoCatalogService
{
    /// <summary>
    /// Gets the databases the signed-in user can access on a cluster.
    /// </summary>
    /// <param name="clusterUri">The absolute HTTPS cluster URI.</param>
    /// <param name="cancellationToken">A token that cancels authentication or discovery.</param>
    /// <returns>The accessible databases in server order.</returns>
    public Task<IReadOnlyList<KustoDatabaseInfo>> GetDatabasesAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the tables and columns for one database.
    /// </summary>
    /// <param name="clusterUri">The absolute HTTPS cluster URI.</param>
    /// <param name="databaseName">The case-sensitive database name.</param>
    /// <param name="cancellationToken">A token that cancels authentication or discovery.</param>
    /// <returns>The immutable database schema.</returns>
    public Task<KustoDatabaseSchema> GetDatabaseSchemaAsync(
        Uri clusterUri,
        string databaseName,
        CancellationToken cancellationToken = default);
}
