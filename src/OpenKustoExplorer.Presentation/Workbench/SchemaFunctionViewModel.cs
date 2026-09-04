using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one stored Kusto function in the Explorer tree.
/// </summary>
public sealed class SchemaFunctionViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaFunctionViewModel"/> class.
    /// </summary>
    /// <param name="functionSchema">The immutable stored-function schema.</param>
    public SchemaFunctionViewModel(KustoFunctionSchema functionSchema)
    {
        ArgumentNullException.ThrowIfNull(functionSchema);

        Name = functionSchema.Name;
        Signature = functionSchema.Signature;
        Folder = functionSchema.Folder;
        Documentation = functionSchema.Documentation;
        ToolTipText = string.IsNullOrWhiteSpace(Documentation)
            ? Signature
            : $"{Signature}{Environment.NewLine}{Documentation}";
    }

    /// <summary>
    /// Gets the case-sensitive function name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the function signature.
    /// </summary>
    public string Signature { get; }

    /// <summary>
    /// Gets the optional server-side function folder.
    /// </summary>
    public string? Folder { get; }

    /// <summary>
    /// Gets the optional function documentation.
    /// </summary>
    public string? Documentation { get; }

    /// <summary>
    /// Gets the combined signature and documentation tooltip.
    /// </summary>
    public string ToolTipText { get; }

    /// <summary>
    /// Determines whether the function matches an Explorer filter.
    /// </summary>
    /// <param name="filterText">The case-insensitive filter text.</param>
    /// <returns><see langword="true"/> when the function should remain visible.</returns>
    internal bool MatchesFilter(string filterText)
    {
        bool matches = string.IsNullOrWhiteSpace(filterText)
            || Signature.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || (Folder?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (Documentation?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false);

        return matches;
    }
}
