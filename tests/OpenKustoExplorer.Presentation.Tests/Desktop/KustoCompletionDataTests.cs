using AvaloniaEdit.Document;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Desktop.Editor;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies Kusto completion insertion behavior in AvaloniaEdit.
/// </summary>
public sealed class KustoCompletionDataTests
{
    /// <summary>
    /// Verifies paired completion text places the caret before the trailing text.
    /// </summary>
    [Fact]
    public void ApplyToDocumentPlacesCaretBetweenBeforeAndAfterText()
    {
        const string QueryPrefix = "let value = print";
        TextDocument document = new(QueryPrefix);
        AnchorSegment completionSegment = new(document, QueryPrefix.Length, 0);
        KustoCompletionData completionData = new(
            new KustoCompletion("Punctuation", "()", "(", ")"));

        int caretOffset = completionData.ApplyToDocument(document, completionSegment);

        Assert.Equal(QueryPrefix + "()", document.Text);
        Assert.Equal(QueryPrefix.Length + 1, caretOffset);
    }
}
