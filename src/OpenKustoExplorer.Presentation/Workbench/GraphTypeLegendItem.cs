namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one semantic node type and its stable graph color.
/// </summary>
public sealed class GraphTypeLegendItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphTypeLegendItem"/> class.
    /// </summary>
    /// <param name="typeName">The exact node type name.</param>
    /// <param name="colorHex">The corresponding palette accent color.</param>
    /// <param name="nodeCount">The number of visible nodes of this type.</param>
    public GraphTypeLegendItem(string typeName, string colorHex, int nodeCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nodeCount);
        TypeName = typeName;
        ColorHex = colorHex;
        NodeCount = nodeCount;
    }

    /// <summary>
    /// Gets the palette accent color.
    /// </summary>
    public string ColorHex { get; }

    /// <summary>
    /// Gets the number of visible nodes of this type.
    /// </summary>
    public int NodeCount { get; }

    /// <summary>
    /// Gets the exact semantic node type.
    /// </summary>
    public string TypeName { get; }
}
