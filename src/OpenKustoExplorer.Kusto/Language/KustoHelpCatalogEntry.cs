namespace OpenKustoExplorer.Kusto.Language;

/// <summary>
/// Describes one locally documented KQL syntax element.
/// </summary>
/// <param name="Title">The canonical KQL spelling.</param>
/// <param name="Kind">The human-readable syntax category.</param>
/// <param name="Signature">The optional usage form.</param>
/// <param name="Description">The concise explanation.</param>
/// <param name="DocumentationUri">The optional official documentation page.</param>
internal sealed record KustoHelpCatalogEntry(
    string Title,
    string Kind,
    string? Signature,
    string Description,
    Uri? DocumentationUri = null);
