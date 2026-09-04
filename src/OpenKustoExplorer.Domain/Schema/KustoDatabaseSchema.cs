namespace OpenKustoExplorer.Domain.Schema;

/// <summary>
/// Describes the active Kusto cluster and database schema used for language analysis.
/// </summary>
public sealed class KustoDatabaseSchema
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDatabaseSchema"/> class.
    /// </summary>
    /// <param name="clusterName">The cluster host name or stable display name.</param>
    /// <param name="databaseName">The case-sensitive database name.</param>
    /// <param name="tables">The tables exposed by the database.</param>
    /// <param name="functions">The stored functions exposed by the database.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="clusterName"/> or <paramref name="databaseName"/> is empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="tables"/> is <see langword="null"/>.</exception>
    public KustoDatabaseSchema(
        string clusterName,
        string databaseName,
        IEnumerable<KustoTableSchema> tables,
        IEnumerable<KustoFunctionSchema>? functions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(tables);

        ClusterName = clusterName;
        DatabaseName = databaseName;
        Tables = Array.AsReadOnly(tables.ToArray());
        Functions = Array.AsReadOnly(functions?.ToArray() ?? []);
    }

    /// <summary>
    /// Gets the cluster host name or stable display name.
    /// </summary>
    public string ClusterName { get; }

    /// <summary>
    /// Gets the case-sensitive database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the tables exposed by the database.
    /// </summary>
    public IReadOnlyList<KustoTableSchema> Tables { get; }

    /// <summary>
    /// Gets the stored functions exposed by the database.
    /// </summary>
    public IReadOnlyList<KustoFunctionSchema> Functions { get; }
}
