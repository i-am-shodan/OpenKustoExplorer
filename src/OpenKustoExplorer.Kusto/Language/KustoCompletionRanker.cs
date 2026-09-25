using Kusto.Language.Editor;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Kusto.Language;

/// <summary>
/// Ranks valid Kusto completions using editor context, in-scope symbols, and real-world KQL usage.
/// </summary>
internal static class KustoCompletionRanker
{
    private static readonly IReadOnlyDictionary<string, int> OperatorOccurrences =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["where"] = 22096,
            ["extend"] = 17845,
            ["summarize"] = 4598,
            ["project"] = 4132,
            ["join"] = 1787,
            ["project-away"] = 1157,
            ["parse"] = 950,
            ["mv-expand"] = 782,
            ["lookup"] = 760,
            ["project-rename"] = 757,
            ["sort"] = 576,
            ["distinct"] = 495,
            ["order"] = 477,
            ["render"] = 336,
            ["top"] = 335,
            ["invoke"] = 321,
            ["mv-apply"] = 260,
            ["make-series"] = 227,
            ["project-reorder"] = 209,
            ["parse-kv"] = 160,
            ["evaluate"] = 138,
            ["union"] = 57,
            ["parse-where"] = 42,
            ["take"] = 38,
        };

    private static readonly IReadOnlyDictionary<string, int> FunctionOccurrences =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["tostring"] = 14522,
            ["iff"] = 4753,
            ["ago"] = 4572,
            ["isnotempty"] = 4222,
            ["split"] = 3805,
            ["array_length"] = 3466,
            ["parse_json"] = 2748,
            ["coalesce"] = 2586,
            ["count"] = 2286,
            ["case"] = 1970,
            ["make_set"] = 1858,
            ["strcat"] = 1651,
            ["toint"] = 1516,
            ["extract"] = 1496,
            ["isnull"] = 1152,
            ["column_ifexists"] = 1142,
            ["max"] = 1105,
            ["isempty"] = 1079,
            ["iif"] = 1048,
            ["now"] = 900,
            ["bin"] = 850,
            ["dcount"] = 800,
            ["arg_max"] = 750,
            ["countif"] = 700,
            ["sum"] = 650,
            ["todatetime"] = 600,
        };

    private static readonly Dictionary<string, string[]> OperatorTransitions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["where"] = ["where", "extend", "summarize", "project", "parse", "join", "distinct", "mv-expand"],
            ["extend"] = ["extend", "where", "project", "summarize", "project-away", "mv-expand", "project-rename", "join", "lookup"],
            ["summarize"] = ["where", "extend", "project", "sort", "order", "render"],
            ["project"] = ["extend", "where", "join", "summarize", "distinct", "sort", "order"],
            ["join"] = ["where", "extend", "project", "summarize"],
            ["mv-expand"] = ["extend", "where", "project", "summarize"],
            ["parse"] = ["extend", "where", "project"],
            ["lookup"] = ["extend", "where", "project"],
        };

    /// <summary>
    /// Orders completion items and maps them to the application completion contract.
    /// </summary>
    /// <param name="items">The valid completion items produced by the Kusto language service.</param>
    /// <param name="text">The complete editor document.</param>
    /// <param name="caretPosition">The completion caret position.</param>
    /// <param name="editStart">The start of the text replaced by a completion.</param>
    /// <param name="isQuerySourceStart">Whether completion occurs at the start of a query expression.</param>
    /// <param name="classifications">Semantic classifications for the document.</param>
    /// <returns>Contextually ranked application completion items.</returns>
    internal static IReadOnlyList<KustoCompletion> Rank(
        IReadOnlyList<CompletionItem> items,
        string text,
        int caretPosition,
        int editStart,
        bool isQuerySourceStart,
        IReadOnlyList<KustoClassification> classifications)
    {
        CompletionContext context = CreateContext(
            text,
            caretPosition,
            editStart,
            isQuerySourceStart,
            classifications);
        List<RankedCompletion> rankedItems = new(items.Count);

        for (int index = 0; index < items.Count; index++)
        {
            CompletionItem item = items[index];
            double priority = GetPriority(item, index, context);
            rankedItems.Add(new RankedCompletion(item, index, priority));
        }

        return rankedItems
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => new KustoCompletion(
                item.Item.Kind.ToString(),
                item.Item.DisplayText,
                item.Item.BeforeText,
                item.Item.AfterText,
                item.Priority,
                item.Item.MatchText))
            .ToArray();
    }

    private static CompletionContext CreateContext(
        string text,
        int caretPosition,
        int editStart,
        bool isQuerySourceStart,
        IReadOnlyList<KustoClassification> classifications)
    {
        int safeCaretPosition = Math.Clamp(caretPosition, 0, text.Length);
        int safeEditStart = Math.Clamp(editStart, 0, safeCaretPosition);
        int previousPosition = safeEditStart - 1;

        while (previousPosition >= 0 && char.IsWhiteSpace(text[previousPosition]))
        {
            previousPosition--;
        }

        bool isPipelineOperator = previousPosition >= 0 && text[previousPosition] == '|';
        string typedText = text.Substring(safeEditStart, safeCaretPosition - safeEditStart);
        string? currentOperator = GetOperatorBeforePosition(text, classifications, safeEditStart);
        Dictionary<string, int> symbolOccurrences = GetSymbolOccurrences(
            text,
            safeCaretPosition,
            classifications);

        return new CompletionContext(
            typedText,
            isQuerySourceStart,
            isPipelineOperator,
            currentOperator,
            symbolOccurrences);
    }

    private static double GetPriority(
        CompletionItem item,
        int originalIndex,
        CompletionContext context)
    {
        string completionName = GetCompletionName(item);
        double priority = GetKindPriority(item.Kind, context);
        priority += GetTypedTextPriority(completionName, context.TypedText);
        priority += GetOccurrencePriority(completionName, item.Kind, context.SymbolOccurrences);

        if (context.IsPipelineOperator)
        {
            priority += GetFrequencyPriority(completionName, OperatorOccurrences, 15);
            priority += GetTransitionPriority(context.CurrentOperator, completionName);
        }
        else if (item.Kind is CompletionKind.BuiltInFunction
            or CompletionKind.AggregateFunction)
        {
            priority += GetFrequencyPriority(completionName, FunctionOccurrences, 10);
        }

        return priority + (1d / (originalIndex + 2));
    }

    private static double GetKindPriority(CompletionKind kind, CompletionContext context)
    {
        if (context.IsQuerySourceStart)
        {
            return kind switch
            {
                CompletionKind.Variable => 3500,
                CompletionKind.LocalFunction => 3400,
                CompletionKind.Table => 3300,
                CompletionKind.DatabaseFunction => 3200,
                CompletionKind.MaterialiedView => 3100,
                CompletionKind.StoredQueryResult => 3000,
                CompletionKind.TabularPrefix => 2800,
                CompletionKind.QueryPrefix => 2500,
                CompletionKind.BuiltInFunction => 2200,
                CompletionKind.Keyword => 2000,
                _ => 1000,
            };
        }

        if (context.IsPipelineOperator)
        {
            return kind == CompletionKind.TabularSuffix ? 3000 : 1800;
        }

        if (string.Equals(context.CurrentOperator, "summarize", StringComparison.OrdinalIgnoreCase)
            && kind == CompletionKind.AggregateFunction)
        {
            return 2600;
        }

        return kind switch
        {
            CompletionKind.Variable => 2400,
            CompletionKind.Parameter => 2350,
            CompletionKind.Column => 2300,
            CompletionKind.LocalFunction => 2200,
            CompletionKind.ScalarInfix => 2100,
            CompletionKind.Table => 2000,
            CompletionKind.AggregateFunction => 1900,
            CompletionKind.DatabaseFunction => 1800,
            CompletionKind.TabularSuffix => 1750,
            CompletionKind.ScalarPrefix => 1700,
            CompletionKind.QueryPrefix => 1600,
            CompletionKind.Keyword => 1500,
            CompletionKind.BuiltInFunction => 1300,
            CompletionKind.Punctuation => 1200,
            _ => 1000,
        };
    }

    private static double GetTypedTextPriority(string completionName, string typedText)
    {
        string prefix = typedText.Trim();

        if (prefix.Length == 0)
        {
            return 0;
        }

        if (completionName.Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return 5000;
        }

        if (completionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return 3000;
        }

        int matchPosition = completionName.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        bool startsSegment = matchPosition > 0
            && (completionName[matchPosition - 1] == '_'
                || completionName[matchPosition - 1] == '-');

        if (startsSegment)
        {
            return 1500;
        }

        return matchPosition >= 0 ? 500 : 0;
    }

    private static double GetOccurrencePriority(
        string completionName,
        CompletionKind kind,
        Dictionary<string, int> symbolOccurrences)
    {
        if (!symbolOccurrences.TryGetValue(completionName, out int occurrences))
        {
            return 0;
        }

        int boundedOccurrences = Math.Min(occurrences, 5);
        int occurrenceWeight = kind is CompletionKind.Variable
            or CompletionKind.Parameter
            or CompletionKind.Column
            or CompletionKind.LocalFunction
            ? 45
            : 12;

        return boundedOccurrences * occurrenceWeight;
    }

    private static double GetFrequencyPriority(
        string completionName,
        IReadOnlyDictionary<string, int> occurrences,
        double weight)
    {
        return occurrences.TryGetValue(completionName, out int count)
            ? Math.Log2(count + 1) * weight
            : 0;
    }

    private static double GetTransitionPriority(string? previousOperator, string completionName)
    {
        if (previousOperator is null
            || !OperatorTransitions.TryGetValue(previousOperator, out string[]? preferredOperators))
        {
            return 0;
        }

        int transitionIndex = Array.FindIndex(
            preferredOperators,
            candidate => candidate.Equals(completionName, StringComparison.OrdinalIgnoreCase));

        return transitionIndex >= 0 ? 260 - (transitionIndex * 22) : 0;
    }

    private static string? GetOperatorBeforePosition(
        string text,
        IReadOnlyList<KustoClassification> classifications,
        int position)
    {
        KustoClassification? nearestOperator = classifications
            .Where(classification => classification.Start < position)
            .Where(classification => classification.Kind.Equals("QueryOperator", StringComparison.Ordinal))
            .OrderByDescending(classification => classification.Start)
            .FirstOrDefault();

        return nearestOperator is null
            ? null
            : text.Substring(nearestOperator.Start, nearestOperator.Length);
    }

    private static Dictionary<string, int> GetSymbolOccurrences(
        string text,
        int caretPosition,
        IReadOnlyList<KustoClassification> classifications)
    {
        Dictionary<string, int> occurrences = new(StringComparer.OrdinalIgnoreCase);

        foreach (KustoClassification classification in classifications)
        {
            if (classification.Start < 0
                || classification.Length <= 0
                || classification.Start + classification.Length > caretPosition
                || classification.Start + classification.Length > text.Length)
            {
                continue;
            }

            string symbol = text.Substring(classification.Start, classification.Length);
            occurrences[symbol] = occurrences.GetValueOrDefault(symbol) + 1;
        }

        return occurrences;
    }

    private static string GetCompletionName(CompletionItem item)
    {
        string text = string.IsNullOrWhiteSpace(item.MatchText)
            ? item.DisplayText
            : item.MatchText;
        int start = 0;

        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        int end = start;

        while (end < text.Length
            && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '-'))
        {
            end++;
        }

        return end > start ? text[start..end] : text.Trim();
    }

    private sealed record CompletionContext(
        string TypedText,
        bool IsQuerySourceStart,
        bool IsPipelineOperator,
        string? CurrentOperator,
        Dictionary<string, int> SymbolOccurrences);

    private sealed record RankedCompletion(
        CompletionItem Item,
        int OriginalIndex,
        double Priority);
}
