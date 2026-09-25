using AvaloniaEdit.Document;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Applies programmatic query edits without resetting AvaloniaEdit undo history.
/// </summary>
internal static class KustoEditorDocumentSynchronizer
{
    /// <summary>
    /// Replaces the complete editor document as one undoable operation.
    /// </summary>
    /// <param name="document">The active editor document.</param>
    /// <param name="text">The replacement query text.</param>
    internal static void ApplyUndoableText(TextDocument document, string text)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(text);

        if (!string.Equals(document.Text, text, StringComparison.Ordinal))
        {
            document.Replace(0, document.TextLength, text);
        }
    }
}
