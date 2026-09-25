using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using OpenKustoExplorer.Desktop.Charts;
using OpenKustoExplorer.Kusto.Execution;
using OpenKustoExplorer.Kusto.Language;

namespace OpenKustoExplorer.Architecture.Tests;

/// <summary>
/// Verifies that browser-delivered assemblies remain isolated from native host implementations.
/// </summary>
public sealed class BrowserDependencyTests
{
    /// <summary>
    /// Verifies that Desktop and Browser consume language intelligence from the shared Kusto assembly.
    /// </summary>
    [Fact]
    public void LanguageServiceBelongsToSharedKustoAssembly()
    {
        Assert.Same(typeof(KustoExecutionService).Assembly, typeof(KustoLanguageService).Assembly);
        string[] referencedTypeNames = GetReferencedTypeNames(GetBrowserAssemblyPath());
        Assert.Contains(typeof(KustoLanguageService).FullName, referencedTypeNames);
    }

    /// <summary>
    /// Verifies that the shared Kusto protocol and execution layer remains host-independent.
    /// </summary>
    [Fact]
    public void SharedKustoDoesNotReferenceHostImplementations()
    {
        string[] referencedAssemblyNames = typeof(KustoExecutionService).Assembly
            .GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("OpenKustoExplorer.Infrastructure", referencedAssemblyNames);
        Assert.DoesNotContain("OpenKustoExplorer.Desktop", referencedAssemblyNames);
        Assert.DoesNotContain("OpenKustoExplorer.Browser", referencedAssemblyNames);
        Assert.DoesNotContain("OpenKustoExplorer.Web", referencedAssemblyNames);
        Assert.DoesNotContain("Lucide.Avalonia", referencedAssemblyNames);
        Assert.DoesNotContain("Microsoft.Identity.Client", referencedAssemblyNames);
        Assert.DoesNotContain("Microsoft.Identity.Web", referencedAssemblyNames);
    }

    /// <summary>
    /// Verifies that the shared Avalonia layer does not reference host-specific implementations.
    /// </summary>
    [Fact]
    public void SharedAvaloniaDoesNotReferenceHostImplementations()
    {
        string[] referencedAssemblyNames = typeof(KustoChart).Assembly
            .GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("OpenKustoExplorer.Infrastructure", referencedAssemblyNames);
        Assert.DoesNotContain("OpenKustoExplorer.Desktop", referencedAssemblyNames);
        Assert.DoesNotContain("Lucide.Avalonia", referencedAssemblyNames);
        Assert.DoesNotContain("Microsoft.Identity.Client", referencedAssemblyNames);
        Assert.DoesNotContain("Microsoft.Identity.Web", referencedAssemblyNames);
    }

    /// <summary>
    /// Verifies shared workbench sources are project-local and discovered without Desktop links.
    /// </summary>
    [Fact]
    public void SharedAvaloniaOwnsWorkbenchSources()
    {
        string repositoryRoot = GetRepositoryRoot();
        string sharedProjectPath = Path.Combine(
            repositoryRoot,
            "src",
            "OpenKustoExplorer.Avalonia",
            "OpenKustoExplorer.Avalonia.csproj");
        string projectText = File.ReadAllText(sharedProjectPath);

        Assert.DoesNotContain("OpenKustoExplorer.Desktop", projectText, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            repositoryRoot,
            "src",
            "OpenKustoExplorer.Avalonia",
            "WorkbenchView.axaml")));
        Assert.True(File.Exists(Path.Combine(
            repositoryRoot,
            "src",
            "OpenKustoExplorer.Avalonia",
            "WorkbenchView.axaml.cs")));
        Assert.False(File.Exists(Path.Combine(
            repositoryRoot,
            "src",
            "OpenKustoExplorer.Desktop",
            "MainWindow.axaml")));
    }

    /// <summary>
    /// Verifies that the browser host does not reference native or browser-incompatible implementations.
    /// </summary>
    [Fact]
    public void BrowserDoesNotReferenceNativeImplementations()
    {
        string browserAssemblyPath = GetBrowserAssemblyPath();
        Assert.True(File.Exists(browserAssemblyPath), $"Browser assembly was not built at '{browserAssemblyPath}'.");

        string[] referencedAssemblyNames = GetReferencedAssemblyNames(browserAssemblyPath);

        Assert.DoesNotContain("OpenKustoExplorer.Infrastructure", referencedAssemblyNames);
        Assert.DoesNotContain("OpenKustoExplorer.Desktop", referencedAssemblyNames);
        Assert.DoesNotContain("Lucide.Avalonia", referencedAssemblyNames);
    }

    /// <summary>
    /// Verifies that the Blazor host does not reference native desktop implementations.
    /// </summary>
    [Fact]
    public void WebDoesNotReferenceNativeImplementations()
    {
        string webAssemblyPath = GetProjectAssemblyPath(
            "OpenKustoExplorer.Web",
            "net10.0");
        Assert.True(File.Exists(webAssemblyPath), $"Web assembly was not built at '{webAssemblyPath}'.");

        string[] referencedAssemblyNames = GetReferencedAssemblyNames(webAssemblyPath);

        Assert.DoesNotContain("OpenKustoExplorer.Infrastructure", referencedAssemblyNames);
        Assert.DoesNotContain("OpenKustoExplorer.Desktop", referencedAssemblyNames);
        Assert.DoesNotContain("Lucide.Avalonia", referencedAssemblyNames);
    }

    private static string[] GetReferencedAssemblyNames(string assemblyPath)
    {
        using FileStream assemblyStream = File.OpenRead(assemblyPath);
        using PEReader peReader = new(assemblyStream);
        MetadataReader metadataReader = peReader.GetMetadataReader();
        return metadataReader.AssemblyReferences
            .Select(referenceHandle => metadataReader.GetString(
                metadataReader.GetAssemblyReference(referenceHandle).Name))
            .ToArray();
    }

    private static string[] GetReferencedTypeNames(string assemblyPath)
    {
        using FileStream assemblyStream = File.OpenRead(assemblyPath);
        using PEReader peReader = new(assemblyStream);
        MetadataReader metadataReader = peReader.GetMetadataReader();
        return metadataReader.TypeReferences
            .Select(metadataReader.GetTypeReference)
            .Select(reference => $"{metadataReader.GetString(reference.Namespace)}.{metadataReader.GetString(reference.Name)}")
            .ToArray();
    }

    private static string GetBrowserAssemblyPath()
    {
        return GetProjectAssemblyPath(
            "OpenKustoExplorer.Browser",
            "net10.0-browser");
    }

    private static string GetProjectAssemblyPath(string projectName, string targetFramework)
    {
        DirectoryInfo testOutputDirectory = new(AppContext.BaseDirectory);
        string configuration = testOutputDirectory.Parent?.Name
            ?? throw new InvalidOperationException("The test build configuration could not be determined.");
        string repositoryRoot = GetRepositoryRoot();
        return Path.Combine(
            repositoryRoot,
            "src",
            projectName,
            "bin",
            configuration,
            targetFramework,
            $"{projectName}.dll");
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
