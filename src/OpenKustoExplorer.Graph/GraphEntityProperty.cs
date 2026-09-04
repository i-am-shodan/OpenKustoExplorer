namespace OpenKustoExplorer.Graph;

/// <summary>
/// Presents one retained source property for a graph entity.
/// </summary>
public sealed class GraphEntityProperty
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphEntityProperty"/> class.
    /// </summary>
    /// <param name="name">The source property name.</param>
    /// <param name="value">The retained source property value.</param>
    public GraphEntityProperty(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name.Trim();
        Value = value;
    }

    /// <summary>
    /// Gets the source property name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the retained source property value.
    /// </summary>
    public string Value { get; }
}
