namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Contains generated KQL or validation diagnostics explaining why generation failed.
/// </summary>
public sealed class KustoGeneratedChainQuery
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGeneratedChainQuery"/> class.
    /// </summary>
    /// <param name="queryText">The generated query text, or an empty string on failure.</param>
    /// <param name="diagnostics">Generation or semantic validation diagnostics.</param>
    public KustoGeneratedChainQuery(string queryText, IEnumerable<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(diagnostics);
        QueryText = queryText;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    /// <summary>Gets the generated query text.</summary>
    public string QueryText { get; }

    /// <summary>Gets generation or semantic validation diagnostics.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Gets a value indicating whether valid KQL was generated.</summary>
    public bool Succeeded => QueryText.Length > 0 && Diagnostics.Count == 0;
}
