namespace OpenKustoExplorer.Application.Documents;

/// <summary>
/// Describes one persisted KQL document tab and its execution target.
/// </summary>
public sealed class KustoDocument
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDocument"/> class.
    /// </summary>
    /// <param name="id">The stable document identifier.</param>
    /// <param name="title">The document tab title.</param>
    /// <param name="text">The complete KQL document text.</param>
    /// <param name="caretPosition">The zero-based caret position.</param>
    /// <param name="clusterUri">The associated cluster URI, or <see langword="null"/>.</param>
    /// <param name="databaseName">The associated database name, or <see langword="null"/>.</param>
    /// <param name="tabColor">The user-selected tab accent color.</param>
    /// <param name="groupName">The optional user-defined tab group name.</param>
    /// <param name="useAlternatingRows">Whether alternating result rows are shaded.</param>
    /// <param name="conditionalFormattingRules">The tab-specific conditional-formatting rules.</param>
    public KustoDocument(
        Guid id,
        string title,
        string text,
        int caretPosition,
        Uri? clusterUri,
        string? databaseName,
        KustoDocumentTabColor tabColor = KustoDocumentTabColor.Default,
        string? groupName = null,
        bool useAlternatingRows = true,
        IEnumerable<KustoConditionalFormatRule>? conditionalFormattingRules = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caretPosition);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caretPosition, text.Length);
        if (!Enum.IsDefined(tabColor))
        {
            throw new ArgumentOutOfRangeException(nameof(tabColor));
        }

        bool hasCluster = clusterUri is not null;
        bool hasDatabase = !string.IsNullOrWhiteSpace(databaseName);
        if (hasCluster != hasDatabase)
        {
            throw new ArgumentException("A document target requires both a cluster URI and database name.");
        }

        if (clusterUri is not null && (!clusterUri.IsAbsoluteUri || clusterUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The document cluster URI must be an absolute HTTPS URI.", nameof(clusterUri));
        }

        Id = id;
        Title = title;
        Text = text;
        CaretPosition = caretPosition;
        ClusterUri = clusterUri;
        DatabaseName = databaseName;
        TabColor = tabColor;
        GroupName = string.IsNullOrWhiteSpace(groupName) ? null : groupName.Trim();
        UseAlternatingRows = useAlternatingRows;
        ConditionalFormattingRules = Array.AsReadOnly(conditionalFormattingRules?.ToArray() ?? []);
    }

    /// <summary>
    /// Gets the stable document identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the document tab title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the complete KQL document text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the zero-based caret position.
    /// </summary>
    public int CaretPosition { get; }

    /// <summary>
    /// Gets the associated cluster URI, or <see langword="null"/>.
    /// </summary>
    public Uri? ClusterUri { get; }

    /// <summary>
    /// Gets the associated database name, or <see langword="null"/>.
    /// </summary>
    public string? DatabaseName { get; }

    /// <summary>
    /// Gets the user-selected tab accent color.
    /// </summary>
    public KustoDocumentTabColor TabColor { get; }

    /// <summary>
    /// Gets the optional user-defined tab group name.
    /// </summary>
    public string? GroupName { get; }

    /// <summary>
    /// Gets a value indicating whether alternating result rows are shaded.
    /// </summary>
    public bool UseAlternatingRows { get; }

    /// <summary>
    /// Gets the tab-specific conditional-formatting rules.
    /// </summary>
    public IReadOnlyList<KustoConditionalFormatRule> ConditionalFormattingRules { get; }
}
