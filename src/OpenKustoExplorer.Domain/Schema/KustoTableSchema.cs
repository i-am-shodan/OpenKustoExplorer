namespace OpenKustoExplorer.Domain.Schema;

/// <summary>
/// Describes a table and its columns in a Kusto database.
/// </summary>
public sealed class KustoTableSchema
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoTableSchema"/> class.
    /// </summary>
    /// <param name="name">The case-sensitive table name.</param>
    /// <param name="columns">The columns exposed by the table.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="columns"/> is <see langword="null"/>.</exception>
    public KustoTableSchema(string name, IEnumerable<KustoColumnSchema> columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(columns);

        Name = name;
        Columns = Array.AsReadOnly(columns.ToArray());
    }

    /// <summary>
    /// Gets the case-sensitive table name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the columns exposed by the table.
    /// </summary>
    public IReadOnlyList<KustoColumnSchema> Columns { get; }
}
