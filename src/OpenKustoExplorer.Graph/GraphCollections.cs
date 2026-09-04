using System.Collections.ObjectModel;

namespace OpenKustoExplorer.Graph;

/// <summary>
/// Creates defensive read-only collection snapshots for immutable graph contracts.
/// </summary>
internal static class GraphCollections
{
    /// <summary>
    /// Copies graph properties using ordinal property-name comparison.
    /// </summary>
    /// <param name="properties">The source properties.</param>
    /// <returns>An immutable property snapshot.</returns>
    internal static IReadOnlyDictionary<string, string> CopyProperties(
        IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        Dictionary<string, string> copy = new(properties, StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, string>(copy);
    }

    /// <summary>
    /// Copies nonempty strings while removing ordinal duplicates.
    /// </summary>
    /// <param name="values">The source values.</param>
    /// <returns>An immutable string snapshot.</returns>
    internal static IReadOnlyList<string> CopyStrings(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        string[] copy = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return Array.AsReadOnly(copy);
    }
}
