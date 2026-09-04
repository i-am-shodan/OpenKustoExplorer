namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one text match from an open KQL document tab.
/// </summary>
public sealed class KustoTabSearchResultViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoTabSearchResultViewModel"/> class.
    /// </summary>
    /// <param name="document">The matched document.</param>
    /// <param name="matchStart">The zero-based match position.</param>
    /// <param name="locationText">The human-readable match location.</param>
    /// <param name="previewText">The trimmed matching line.</param>
    internal KustoTabSearchResultViewModel(
        KustoDocumentViewModel document,
        int matchStart,
        string locationText,
        string previewText)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegative(matchStart);
        ArgumentNullException.ThrowIfNull(locationText);
        ArgumentNullException.ThrowIfNull(previewText);

        Document = document;
        MatchStart = matchStart;
        LocationText = locationText;
        PreviewText = previewText;
    }

    /// <summary>
    /// Gets the matched tab title.
    /// </summary>
    public string DocumentTitle => Document.Title;

    /// <summary>
    /// Gets the one-based matching line description.
    /// </summary>
    public string LocationText { get; }

    /// <summary>
    /// Gets the trimmed matching line.
    /// </summary>
    public string PreviewText { get; }

    /// <summary>
    /// Gets the zero-based match position.
    /// </summary>
    public int MatchStart { get; }

    /// <summary>
    /// Gets the matched document.
    /// </summary>
    internal KustoDocumentViewModel Document { get; }
}
