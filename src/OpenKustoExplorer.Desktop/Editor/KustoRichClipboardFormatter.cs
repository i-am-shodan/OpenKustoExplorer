using System.Globalization;
using System.Net;
using System.Text;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Creates clipboard-safe rich KQL from semantic classifications.
/// </summary>
internal static class KustoRichClipboardFormatter
{
    /// <summary>
    /// Creates plain text, MIME HTML, and Windows CF_HTML for one document selection.
    /// </summary>
    /// <param name="documentText">The complete immutable editor text.</param>
    /// <param name="selectionStart">The zero-based selection start.</param>
    /// <param name="selectionLength">The selected character count.</param>
    /// <param name="classifications">The active semantic classifications.</param>
    /// <param name="colorResolver">Resolves a classification kind to a CSS color.</param>
    /// <param name="foregroundColor">The default editor foreground CSS color.</param>
    /// <param name="backgroundColor">The editor surface CSS color.</param>
    /// <returns>All supported clipboard representations.</returns>
    public static KustoRichClipboardContent Create(
        string documentText,
        int selectionStart,
        int selectionLength,
        IReadOnlyList<KustoClassification> classifications,
        Func<string, string?> colorResolver,
        string foregroundColor,
        string backgroundColor)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        ArgumentOutOfRangeException.ThrowIfNegative(selectionStart);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(selectionLength);
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentNullException.ThrowIfNull(colorResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(foregroundColor);
        ArgumentException.ThrowIfNullOrWhiteSpace(backgroundColor);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(selectionStart, documentText.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            selectionLength,
            documentText.Length - selectionStart);

        int selectionEnd = selectionStart + selectionLength;
        string plainText = documentText.Substring(selectionStart, selectionLength);
        StringBuilder highlightedText = new();
        int cursor = selectionStart;

        foreach (KustoClassification classification in classifications
            .OrderBy(classification => classification.Start)
            .ThenBy(classification => classification.Length))
        {
            int classifiedStart = Math.Max(selectionStart, classification.Start);
            int classifiedEnd = Math.Min(selectionEnd, classification.Start + classification.Length);

            if (classifiedEnd <= cursor || classifiedStart >= selectionEnd)
            {
                continue;
            }

            classifiedStart = Math.Max(classifiedStart, cursor);
            AppendEncoded(highlightedText, documentText, cursor, classifiedStart - cursor);
            string? color = colorResolver(classification.Kind);

            if (string.IsNullOrWhiteSpace(color))
            {
                AppendEncoded(highlightedText, documentText, classifiedStart, classifiedEnd - classifiedStart);
            }
            else
            {
                highlightedText.Append("<span style=\"color:")
                    .Append(WebUtility.HtmlEncode(color))
                    .Append("\">");
                AppendEncoded(highlightedText, documentText, classifiedStart, classifiedEnd - classifiedStart);
                highlightedText.Append("</span>");
            }

            cursor = classifiedEnd;
        }

        AppendEncoded(highlightedText, documentText, cursor, selectionEnd - cursor);
        string fragment = string.Create(
            CultureInfo.InvariantCulture,
            $"<pre style=\"margin:0;padding:8px;background-color:{backgroundColor};color:{foregroundColor};font-family:'Cascadia Code','JetBrains Mono','Noto Sans Mono',monospace;font-size:10pt;white-space:pre-wrap\">{highlightedText}</pre>");
        (string html, string windowsHtml) = CreateHtmlDocuments(fragment);
        return new KustoRichClipboardContent(plainText, html, windowsHtml);
    }

    private static void AppendEncoded(
        StringBuilder builder,
        string text,
        int start,
        int length)
    {
        if (length > 0)
        {
            builder.Append(WebUtility.HtmlEncode(text.Substring(start, length)));
        }
    }

    private static string BuildHeader(
        int startHtml,
        int endHtml,
        int startFragment,
        int endFragment)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Version:1.0\r\nStartHTML:{startHtml:D10}\r\nEndHTML:{endHtml:D10}\r\nStartFragment:{startFragment:D10}\r\nEndFragment:{endFragment:D10}\r\n");
    }

    private static (string Html, string WindowsHtml) CreateHtmlDocuments(string fragment)
    {
        const string HtmlStart = "<!DOCTYPE html>\r\n<html><body><!--StartFragment-->";
        const string HtmlEnd = "<!--EndFragment--></body></html>\r\n";
        string placeholderHeader = BuildHeader(0, 0, 0, 0);
        int startHtml = Encoding.UTF8.GetByteCount(placeholderHeader);
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(HtmlStart);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(HtmlEnd);
        string html = $"{HtmlStart}{fragment}{HtmlEnd}";
        string windowsHtml = $"{BuildHeader(startHtml, endHtml, startFragment, endFragment)}{html}";
        return (html, windowsHtml);
    }
}
