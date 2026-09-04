using System.Reflection;

namespace OpenKustoExplorer.Presentation;

/// <summary>
/// Exposes the presentation assembly for architecture and composition validation.
/// </summary>
public static class PresentationAssembly
{
    /// <summary>
    /// Gets the assembly that contains the presentation layer.
    /// </summary>
    public static Assembly Value { get; } = typeof(PresentationAssembly).Assembly;
}
