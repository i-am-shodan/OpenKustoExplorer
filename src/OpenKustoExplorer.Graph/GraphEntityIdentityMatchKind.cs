namespace OpenKustoExplorer.Graph;

/// <summary>
/// Describes the value that caused graph entity identities to look equivalent.
/// </summary>
public enum GraphEntityIdentityMatchKind
{
    /// <summary>
    /// The canonical identifiers match after case and surrounding whitespace normalization.
    /// </summary>
    CanonicalId,

    /// <summary>
    /// The display labels match after case and surrounding whitespace normalization.
    /// </summary>
    DisplayLabel,
}
