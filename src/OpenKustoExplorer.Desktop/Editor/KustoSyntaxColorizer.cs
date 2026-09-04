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
    private IReadOnlyList<KustoClassification> classifications = Array.Empty<KustoClassification>();

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
        this.classifications = classifications;
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

    /// <inheritdoc />
    protected override void ColorizeLine(DocumentLine line)
    {
        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        foreach (KustoClassification classification in classifications)
        {
            int classificationEnd = classification.Start + classification.Length;
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
