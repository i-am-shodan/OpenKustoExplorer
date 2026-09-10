using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lucide.Avalonia;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Presents a completion with a compact category icon and label.
/// </summary>
internal sealed class KustoCompletionItemContent : Grid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCompletionItemContent"/> class.
    /// </summary>
    /// <param name="completion">The completion shown in the popup.</param>
    public KustoCompletionItemContent(KustoCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        CompletionCategory category = GetCategory(completion.Kind);
        ColumnDefinitions = new ColumnDefinitions("28,*,Auto");
        ColumnSpacing = 8;
        Classes.Add("kustoCompletionItem");

        LucideIcon icon = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Kind = category.Icon,
            Size = 15,
            StrokeWidth = 1.8,
        };
        icon.Classes.Add("completionKindIcon");
        icon.Classes.Add(category.StyleClass);
        ToolTip.SetTip(icon, category.Label);

        TextBlock displayText = new()
        {
            Text = completion.DisplayText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        displayText.Classes.Add("completionText");
        ToolTip.SetTip(displayText, completion.DisplayText);

        TextBlock categoryText = new()
        {
            Text = category.Label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        categoryText.Classes.Add("completionKindLabel");

        SetColumn(icon, 0);
        SetColumn(displayText, 1);
        SetColumn(categoryText, 2);
        Children.Add(icon);
        Children.Add(displayText);
        Children.Add(categoryText);
    }

    private static CompletionCategory GetCategory(string kind)
    {
        return kind switch
        {
            "Table" => new(LucideIconKind.Table2, "table", "completionEntity"),
            "MaterialiedView" => new(LucideIconKind.TableProperties, "materialized view", "completionEntity"),
            "StoredQueryResult" => new(LucideIconKind.Table2, "stored result", "completionEntity"),
            "Column" => new(LucideIconKind.Columns3, "column", "completionName"),
            "Variable" => new(LucideIconKind.Variable, "variable", "completionVariable"),
            "Parameter" => new(LucideIconKind.Variable, "parameter", "completionVariable"),
            "LocalFunction" => new(LucideIconKind.SquareFunction, "local function", "completionFunction"),
            "DatabaseFunction" => new(LucideIconKind.SquareFunction, "database function", "completionFunction"),
            "BuiltInFunction" => new(LucideIconKind.SquareFunction, "function", "completionFunction"),
            "AggregateFunction" => new(LucideIconKind.Sigma, "aggregate", "completionFunction"),
            "TabularPrefix" or "TabularSuffix" or "QueryPrefix" or "CommandPrefix" =>
                new(LucideIconKind.Workflow, "operator", "completionOperator"),
            "ScalarInfix" => new(LucideIconKind.Code, "operator", "completionOperator"),
            "Keyword" => new(LucideIconKind.KeyRound, "keyword", "completionKeyword"),
            "Punctuation" or "Syntax" => new(LucideIconKind.Parentheses, "syntax", "completionSyntax"),
            "ScalarType" => new(LucideIconKind.Braces, "type", "completionSyntax"),
            "Database" or "Cluster" => new(LucideIconKind.Database, kind.ToLowerInvariant(), "completionEntity"),
            "RenderChart" => new(LucideIconKind.ChartNoAxesCombined, "chart", "completionOperator"),
            "Graph" => new(LucideIconKind.Workflow, "graph", "completionEntity"),
            _ => new(LucideIconKind.Code, kind.ToLowerInvariant(), "completionSyntax"),
        };
    }

    private sealed record CompletionCategory(
        LucideIconKind Icon,
        string Label,
        string StyleClass);
}
