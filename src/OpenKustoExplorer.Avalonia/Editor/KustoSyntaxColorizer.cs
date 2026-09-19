using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Applies Kusto semantic classifications to visible AvaloniaEdit document lines.
/// </summary>
internal sealed class KustoSyntaxColorizer : DocumentColorizingTransformer
{
    private readonly Func<string, IBrush?> brushResolver;
    private KustoClassification[] classifications = [];
    private int[] maximumClassificationEnds = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoSyntaxColorizer"/> class.
    /// </summary>
    /// <param name="brushResolver">Resolves a semantic brush resource for the active theme.</param>
    public KustoSyntaxColorizer(Func<string, IBrush?> brushResolver)
    {
        ArgumentNullException.ThrowIfNull(brushResolver);
        this.brushResolver = brushResolver;
    }

    /// <summary>
    /// Replaces the immutable classifications used during subsequent line rendering.
    /// </summary>
    /// <param name="classifications">The complete document classifications.</param>
    public void Update(IReadOnlyList<KustoClassification> classifications)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        this.classifications = classifications
            .Where(classification => classification.Length > 0)
            .OrderBy(classification => classification.Start)
            .ThenBy(classification => classification.Length)
            .ToArray();
        maximumClassificationEnds = new int[this.classifications.Length];
        int maximumEnd = 0;
        for (int index = 0; index < this.classifications.Length; index++)
        {
            maximumEnd = Math.Max(maximumEnd, GetClassificationEnd(this.classifications[index]));
            maximumClassificationEnds[index] = maximumEnd;
        }
    }

    /// <summary>
    /// Gets the theme brush resource used for a semantic classification.
    /// </summary>
    /// <param name="classificationKind">The stable language-service classification kind.</param>
    /// <returns>The resource key, or <see langword="null"/> for unstyled text.</returns>
    internal static string? GetBrushResourceKey(string classificationKind)
    {
        return classificationKind switch
        {
            "Comment" => "SyntaxCommentBrush",
            "Command" or "Keyword" or "QueryOperator" => "SyntaxKeywordBrush",
            "Database" or "Table" => "SyntaxEntityBrush",
            "Column" or "DataType" or "Option" => "SyntaxNameBrush",
            "Function" or "Plugin" => "SyntaxFunctionBrush",
            "GuidLiteral" or "NumberLiteral" or "StringLiteral" or "TimeSpanLiteral" => "SyntaxLiteralBrush",
            "ClientParameter" or "Parameter" or "Variable" => "SyntaxVariableBrush",
            _ => null,
        };
    }

    /// <summary>
    /// Gets a classification's exclusive end without integer overflow.
    /// </summary>
    /// <param name="classification">The classified source span.</param>
    /// <returns>The exclusive source end.</returns>
    internal static int GetClassificationEnd(KustoClassification classification)
    {
        return (int)Math.Min(int.MaxValue, (long)classification.Start + classification.Length);
    }

    /// <summary>
    /// Finds the first classification that can intersect a source position.
    /// </summary>
    /// <param name="lineStart">The visible line's source start.</param>
    /// <returns>The first candidate index, or the classification count when none can intersect.</returns>
    internal int FindFirstCandidate(int lineStart)
    {
        int low = 0;
        int high = classifications.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (maximumClassificationEnds[middle] <= lineStart)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <inheritdoc />
    protected override void ColorizeLine(DocumentLine line)
    {
        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        int index = FindFirstCandidate(lineStart);
        while (index < classifications.Length)
        {
            KustoClassification classification = classifications[index++];
            if (classification.Start >= lineEnd)
            {
                break;
            }

            int classificationEnd = GetClassificationEnd(classification);
            int colorStart = Math.Max(lineStart, classification.Start);
            int colorEnd = Math.Min(lineEnd, classificationEnd);
            IBrush? brush = GetBrush(classification.Kind);

            if (brush is not null && colorStart < colorEnd)
            {
                ChangeLinePart(
                    colorStart,
                    colorEnd,
                    visualElement => visualElement.TextRunProperties.SetForegroundBrush(brush));
            }
        }
    }

    private IBrush? GetBrush(string classificationKind)
    {
        string? resourceKey = GetBrushResourceKey(classificationKind);
        IBrush? brush = resourceKey is null ? null : brushResolver(resourceKey);

        return brush;
    }
}
