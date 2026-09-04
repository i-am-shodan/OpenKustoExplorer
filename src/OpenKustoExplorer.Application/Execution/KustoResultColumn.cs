namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Describes one column in a Kusto result table.
/// </summary>
public sealed class KustoResultColumn
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultColumn"/> class.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="typeName">The server-reported CLR type name.</param>
    public KustoResultColumn(string name, string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        Name = name;
        TypeName = typeName;
    }

    /// <summary>
    /// Gets the column name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the server-reported CLR type name.
    /// </summary>
    public string TypeName { get; }
}
