using OpenKustoExplorer.Presentation;

namespace OpenKustoExplorer.Presentation.Tests.Architecture;

/// <summary>
/// Verifies the inward-only dependency direction of the presentation layer.
/// </summary>
public sealed class PresentationDependencyTests
{
    /// <summary>
    /// Verifies that presentation does not reference infrastructure or the desktop composition root.
    /// </summary>
    [Fact]
    public void PresentationDoesNotReferenceOuterLayers()
    {
        string[] referencedAssemblyNames = PresentationAssembly.Value
            .GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("OpenKustoExplorer.Infrastructure", referencedAssemblyNames);
        Assert.DoesNotContain("OpenKustoExplorer.Desktop", referencedAssemblyNames);
    }
}
