using System.Text;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Desktop.Editor;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies rich KQL clipboard serialization.
/// </summary>
public sealed class KustoRichClipboardFormatterTests
{
    /// <summary>
    /// Verifies selected KQL retains semantic colors, HTML escaping, and a plain-text fallback.
    /// </summary>
    [Fact]
    public void CreatePreservesSelectionAndSemanticColors()
    {
        const string Query = "StormEvents | where State == \"Téxas\"";
        int selectionStart = Query.IndexOf("where", StringComparison.Ordinal);
        int stringStart = Query.IndexOf('"');
        KustoClassification[] classifications =
        [
            new KustoClassification("Table", 0, "StormEvents".Length),
            new KustoClassification("QueryOperator", selectionStart, "where".Length),
            new KustoClassification("StringLiteral", stringStart, "\"Téxas\"".Length),
        ];

        KustoRichClipboardContent content = KustoRichClipboardFormatter.Create(
            Query,
            selectionStart,
            Query.Length - selectionStart,
            classifications,
            kind => kind switch
            {
                "QueryOperator" => "#205FA2",
                "StringLiteral" => "#167A4E",
                _ => null,
            },
            "#18242D",
            "#F7F9FA");

        Assert.Equal("where State == \"Téxas\"", content.PlainText);
        Assert.DoesNotContain("StormEvents", content.Html, StringComparison.Ordinal);
        Assert.Contains("<span style=\"color:#205FA2\">where</span>", content.Html, StringComparison.Ordinal);
        Assert.Contains("<span style=\"color:#167A4E\">&quot;T&#233;xas&quot;</span>", content.Html, StringComparison.Ordinal);
        Assert.Contains("background-color:#F7F9FA", content.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Windows CF_HTML offsets address the UTF-8 HTML and selected fragment exactly.
    /// </summary>
    [Fact]
    public void CreateBuildsValidUtf8CfHtmlOffsets()
    {
        const string Query = "print message = \"Téxas\"";
        KustoRichClipboardContent content = KustoRichClipboardFormatter.Create(
            Query,
            0,
            Query.Length,
            [],
            _ => null,
            "#FFFFFF",
            "#000000");
        byte[] bytes = Encoding.UTF8.GetBytes(content.WindowsHtml);
        int startHtml = ReadOffset(content.WindowsHtml, "StartHTML:");
        int endHtml = ReadOffset(content.WindowsHtml, "EndHTML:");
        int startFragment = ReadOffset(content.WindowsHtml, "StartFragment:");
        int endFragment = ReadOffset(content.WindowsHtml, "EndFragment:");

        string html = Encoding.UTF8.GetString(bytes[startHtml..endHtml]);
        string fragment = Encoding.UTF8.GetString(bytes[startFragment..endFragment]);
        Assert.Equal(content.Html, html);
        Assert.StartsWith("<pre", fragment, StringComparison.Ordinal);
        Assert.EndsWith("</pre>", fragment, StringComparison.Ordinal);
        Assert.Contains("T&#233;xas", fragment, StringComparison.Ordinal);
    }

    private static int ReadOffset(string content, string fieldName)
    {
        int valueStart = content.IndexOf(fieldName, StringComparison.Ordinal) + fieldName.Length;
        return int.Parse(content.AsSpan(valueStart, 10), System.Globalization.CultureInfo.InvariantCulture);
    }
}
