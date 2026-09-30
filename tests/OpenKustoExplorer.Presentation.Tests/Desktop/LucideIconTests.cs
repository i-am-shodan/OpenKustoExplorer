using Lucide.Avalonia;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies the AOT-safe Lucide icon resource.
/// </summary>
public sealed class LucideIconTests
{
    /// <summary>
    /// Verifies every supported icon kind has embedded path data.
    /// </summary>
    [Fact]
    public void EveryIconKindHasEmbeddedPath()
    {
        const string ResourceName = "OpenKustoExplorer.Avalonia.Icons.lucide-paths.txt";
        using Stream stream = typeof(LucideIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Icon resource '{ResourceName}' was not found.");
        using StreamReader reader = new(stream);
        Dictionary<string, string> paths = [];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            int separatorIndex = line.IndexOf('\t', StringComparison.Ordinal);
            Assert.True(separatorIndex > 0);
            paths.Add(line[..separatorIndex], line[(separatorIndex + 1)..]);
        }

        Assert.Equal(
            Enum.GetNames<LucideIconKind>().Order(StringComparer.Ordinal),
            paths.Keys.Order(StringComparer.Ordinal));
        Assert.All(paths.Values, path => Assert.False(string.IsNullOrWhiteSpace(path)));
    }
}
