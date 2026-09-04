namespace OpenKustoExplorer.Domain.Schema;

/// <summary>
/// Describes one stored Kusto function exposed by a database.
/// </summary>
public sealed class KustoFunctionSchema
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoFunctionSchema"/> class.
    /// </summary>
    /// <param name="name">The case-sensitive function name.</param>
    /// <param name="parameters">The function parameter declaration.</param>
    /// <param name="body">The stored function body.</param>
    /// <param name="folder">The optional server-side function folder.</param>
    /// <param name="documentation">The optional function documentation.</param>
    public KustoFunctionSchema(
        string name,
        string? parameters,
        string? body,
        string? folder,
        string? documentation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Parameters = string.IsNullOrWhiteSpace(parameters) ? "()" : parameters.Trim();
        Body = body ?? string.Empty;
        Folder = Normalize(folder);
        Documentation = Normalize(documentation);
    }

    /// <summary>
    /// Gets the case-sensitive function name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the function parameter declaration.
    /// </summary>
    public string Parameters { get; }

    /// <summary>
    /// Gets the stored function body.
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// Gets the optional server-side function folder.
    /// </summary>
    public string? Folder { get; }

    /// <summary>
    /// Gets the optional function documentation.
    /// </summary>
    public string? Documentation { get; }

    /// <summary>
    /// Gets the function signature shown in Explorer.
    /// </summary>
    public string Signature => $"{Name}{Parameters}";

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
