using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Adapts an application completion item to the AvaloniaEdit completion contract.
/// </summary>
internal sealed class KustoCompletionData : ICompletionData
{
    private readonly KustoCompletion completion;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCompletionData"/> class.
    /// </summary>
    /// <param name="completion">The immutable Kusto completion item.</param>
    public KustoCompletionData(KustoCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        this.completion = completion;
    }

    /// <inheritdoc />
    public IImage? Image => null;

    /// <inheritdoc />
    public string Text => completion.DisplayText;

    /// <inheritdoc />
    public object Content => completion.DisplayText;

    /// <inheritdoc />
    public object Description => completion.Kind;

    /// <inheritdoc />
    public double Priority => completion.Priority;

    /// <inheritdoc />
    public void Complete(
        TextArea textArea,
        ISegment completionSegment,
        EventArgs insertionRequestEventArgs)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        ArgumentNullException.ThrowIfNull(completionSegment);
        ArgumentNullException.ThrowIfNull(insertionRequestEventArgs);

        string insertionText = completion.BeforeText + completion.AfterText;
        textArea.Document.Replace(completionSegment.Offset, completionSegment.Length, insertionText);
        textArea.Caret.Offset = completionSegment.Offset + completion.BeforeText.Length;
    }
}
