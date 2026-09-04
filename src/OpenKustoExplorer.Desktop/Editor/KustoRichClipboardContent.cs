namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Contains plain and rich representations of one copied KQL selection.
/// </summary>
internal sealed class KustoRichClipboardContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRichClipboardContent"/> class.
    /// </summary>
    /// <param name="plainText">The selected KQL text.</param>
    /// <param name="html">The complete MIME HTML document.</param>
    /// <param name="windowsHtml">The Windows CF_HTML payload.</param>
    public KustoRichClipboardContent(string plainText, string html, string windowsHtml)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsHtml);
        PlainText = plainText;
        Html = html;
        WindowsHtml = windowsHtml;
    }

    /// <summary>
    /// Gets the selected KQL text.
    /// </summary>
    public string PlainText { get; }

    /// <summary>
    /// Gets the complete MIME HTML document.
    /// </summary>
    public string Html { get; }

    /// <summary>
    /// Gets the Windows CF_HTML payload with UTF-8 byte offsets.
    /// </summary>
    public string WindowsHtml { get; }
}
