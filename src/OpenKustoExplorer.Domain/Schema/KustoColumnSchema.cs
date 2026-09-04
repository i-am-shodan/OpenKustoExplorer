namespace OpenKustoExplorer.Domain.Schema;

/// <summary>
/// Describes a column exposed by a Kusto table.
/// </summary>
public sealed class KustoColumnSchema
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoColumnSchema"/> class.
    /// </summary>
    /// <param name="name">The case-sensitive column name.</param>
    /// <param name="type">The column scalar type.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public KustoColumnSchema(string name, KustoScalarType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Type = type;
    }

    /// <summary>
    /// Gets the case-sensitive column name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the column scalar type.
    /// </summary>
    public KustoScalarType Type { get; }
}
