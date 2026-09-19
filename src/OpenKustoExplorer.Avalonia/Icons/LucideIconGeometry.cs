using System.Collections.Frozen;
using System.Reflection;
using Avalonia.Media;

namespace Lucide.Avalonia;

/// <summary>
/// Loads and caches uncompressed Lucide geometry used by the workbench.
/// </summary>
internal static class LucideIconGeometry
{
    private const string ResourceName = "OpenKustoExplorer.Avalonia.Icons.lucide-paths.txt";
    private static readonly Dictionary<LucideIconKind, Geometry> GeometryCache = [];
    private static readonly Lazy<FrozenDictionary<LucideIconKind, string>> GeometryData = new(LoadGeometryData);

    /// <summary>
    /// Gets parsed geometry for one icon kind.
    /// </summary>
    /// <param name="kind">The requested icon kind.</param>
    /// <returns>The cached icon geometry.</returns>
    internal static Geometry Get(LucideIconKind kind)
    {
        if (!GeometryCache.TryGetValue(kind, out Geometry? geometry))
        {
            geometry = Geometry.Parse(GeometryData.Value[kind]);
            GeometryCache.Add(kind, geometry);
        }

        return geometry;
    }

    private static FrozenDictionary<LucideIconKind, string> LoadGeometryData()
    {
        Assembly assembly = typeof(LucideIconGeometry).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Icon resource '{ResourceName}' was not found.");
        using StreamReader reader = new(stream);
        Dictionary<LucideIconKind, string> data = [];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            int separatorIndex = line.IndexOf('\t', StringComparison.Ordinal);
            if (separatorIndex <= 0
                || !Enum.TryParse(line.AsSpan(0, separatorIndex), out LucideIconKind kind))
            {
                throw new InvalidDataException("The embedded icon resource contains an invalid entry.");
            }

            data.Add(kind, line[(separatorIndex + 1)..]);
        }

        return data.ToFrozenDictionary();
    }
}
