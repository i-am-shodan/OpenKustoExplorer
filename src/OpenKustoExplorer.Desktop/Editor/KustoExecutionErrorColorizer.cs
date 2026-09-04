using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Applies a non-destructive background highlight to the latest failed query source.
/// </summary>
internal sealed class KustoExecutionErrorColorizer : DocumentColorizingTransformer
{
    private readonly Func<string, IBrush?> brushResolver;
    private KustoQueryErrorHighlight? highlight;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoExecutionErrorColorizer"/> class.
    /// </summary>
    /// <param name="brushResolver">Resolves the execution-error background for the active theme.</param>
    public KustoExecutionErrorColorizer(Func<string, IBrush?> brushResolver)
    {
        ArgumentNullException.ThrowIfNull(brushResolver);
        this.brushResolver = brushResolver;
    }

    /// <summary>
    /// Replaces the source span highlighted during subsequent line rendering.
    /// </summary>
    /// <param name="newHighlight">The active execution-error span, or <see langword="null"/>.</param>
    public void Update(KustoQueryErrorHighlight? newHighlight)
    {
        highlight = newHighlight;
    }

    /// <inheritdoc />
    protected override void ColorizeLine(DocumentLine line)
    {
        if (highlight is null)
        {
            return;
        }

        int highlightEnd = highlight.Start + highlight.Length;
        int colorStart = Math.Max(line.Offset, highlight.Start);
        int colorEnd = Math.Min(line.EndOffset, highlightEnd);
        IBrush? brush = brushResolver("QueryErrorHighlightBrush");

        if (brush is not null && colorStart < colorEnd)
        {
            ChangeLinePart(
                colorStart,
                colorEnd,
                visualElement => visualElement.TextRunProperties.SetBackgroundBrush(brush));
        }
    }
}
