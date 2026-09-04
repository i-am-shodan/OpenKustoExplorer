namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Describes one node label or relationship type available to openCypher.
/// </summary>
public sealed class GraphQuerySchemaEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphQuerySchemaEntry"/> class.
    /// </summary>
    /// <param name="name">The exact node label or relationship type.</param>
    /// <param name="count">The active snapshot element count.</param>
    /// <param name="propertyNames">Bounded latest-observation property names.</param>
    public GraphQuerySchemaEntry(string name, long count, IEnumerable<string> propertyNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(propertyNames);
        Name = name.Trim();
        Count = count;
        PropertyNames = Array.AsReadOnly(
            propertyNames
                .Where(propertyName => !string.IsNullOrWhiteSpace(propertyName))
                .Select(propertyName => propertyName.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// Gets the exact node label or relationship type.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the active snapshot element count.
    /// </summary>
    public long Count { get; }

    /// <summary>
    /// Gets bounded latest-observation property names.
    /// </summary>
    public IReadOnlyList<string> PropertyNames { get; }
}
