namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Describes contextual help for one KQL syntax element.
/// </summary>
public sealed class KustoSyntaxHelp
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoSyntaxHelp"/> class.
    /// </summary>
    /// <param name="title">The syntax element displayed as the help title.</param>
    /// <param name="kind">The human-readable syntax category.</param>
    /// <param name="signature">The optional signature or usage form.</param>
    /// <param name="description">The concise explanation of the syntax element.</param>
    /// <param name="start">The zero-based start of the syntax element.</param>
    /// <param name="length">The length of the syntax element.</param>
    /// <param name="documentationUri">The optional official documentation page.</param>
    public KustoSyntaxHelp(
        string title,
        string kind,
        string? signature,
        string description,
        int start,
        int length,
        Uri? documentationUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        Title = title;
        Kind = kind;
        Signature = string.IsNullOrWhiteSpace(signature) ? null : signature.Trim();
        Description = description;
        Start = start;
        Length = length;
        DocumentationUri = documentationUri;
    }

    /// <summary>
    /// Gets the syntax element displayed as the help title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the human-readable syntax category.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the optional signature or usage form.
    /// </summary>
    public string? Signature { get; }

    /// <summary>
    /// Gets the concise explanation of the syntax element.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the optional official documentation page.
    /// </summary>
    public Uri? DocumentationUri { get; }

    /// <summary>
    /// Gets the zero-based start of the syntax element.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the length of the syntax element.
    /// </summary>
    public int Length { get; }
}
