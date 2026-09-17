using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Application.Tests.Architecture;

/// <summary>
/// Verifies the inward-only dependency direction of the application layer.
/// </summary>
public sealed class ApplicationDependencyTests
{
    /// <summary>
    /// Verifies that the application layer does not reference outer implementation layers.
    /// </summary>
    [Fact]
    public void ApplicationDoesNotReferenceOuterLayers()
    {
        string[] referencedAssemblyNames = typeof(IKustoLanguageService).Assembly
            .GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("OpenKustoExplorer.Infrastructure", referencedAssemblyNames);
        Assert.DoesNotContain("OpenKustoExplorer.Presentation", referencedAssemblyNames);
        Assert.DoesNotContain("OpenKustoExplorer", referencedAssemblyNames);
    }
}
