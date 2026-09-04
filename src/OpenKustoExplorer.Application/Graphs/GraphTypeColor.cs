namespace OpenKustoExplorer.Application.Graphs;

/// <summary>
/// Maps graph semantic type names to stable palette entries shared by rendering and legends.
/// </summary>
public static class GraphTypeColor
{
    /// <summary>
    /// Gets the number of automatic graph palette entries.
    /// </summary>
    public const int Count = 10;

    private static readonly string[] LightAccentHexValues =
    [
        "#2A70AB",
        "#267D52",
        "#B44247",
        "#97670E",
        "#7048A3",
        "#207A82",
        "#A03A70",
        "#62771E",
        "#4855AD",
        "#A45326",
    ];

    /// <summary>
    /// Gets a stable palette index for an exact semantic type name.
    /// </summary>
    /// <param name="typeName">The source-provided semantic type.</param>
    /// <returns>A zero-based palette index.</returns>
    public static int GetIndex(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        const uint OffsetBasis = 2166136261;
        const uint Prime = 16777619;
        uint hash = OffsetBasis;

        foreach (char character in typeName)
        {
            hash ^= char.ToUpperInvariant(character);
            hash *= Prime;
        }

        hash ^= 0xFF;
        hash *= Prime;
        return (int)(hash % Count);
    }

    /// <summary>
    /// Gets the light-theme accent color for a palette index.
    /// </summary>
    /// <param name="colorIndex">The zero-based palette index.</param>
    /// <returns>An RGB hexadecimal color.</returns>
    public static string GetLightAccentHex(int colorIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(colorIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(colorIndex, Count);
        return LightAccentHexValues[colorIndex];
    }
}
