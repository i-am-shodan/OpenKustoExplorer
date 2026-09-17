using System.Globalization;
using System.Text.RegularExpressions;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Maps service query diagnostics into the active document.
/// </summary>
internal static partial class KustoQueryErrorLocator
{
    private const int RegexTimeoutMilliseconds = 1_000;

    /// <summary>
    /// Locates the failed line or executed statement when the exception describes a source error.
    /// </summary>
    /// <param name="exception">The query execution failure.</param>
    /// <param name="selection">The independently executed query block.</param>
    /// <param name="documentText">The complete document snapshot that was executed.</param>
    /// <returns>The document highlight, or <see langword="null"/> for non-source failures.</returns>
    internal static KustoQueryErrorHighlight? Locate(
        Exception exception,
        KustoQuerySelection? selection,
        string documentText)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(documentText);

        if (selection is null || !IsValidSelection(selection, documentText))
        {
            return null;
        }

        string message = exception.Message;
        Match locationMatch = LinePositionRegex().Match(message);

        if (locationMatch.Success
            && int.TryParse(
                locationMatch.Groups["line"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int relativeLine)
            && int.TryParse(
                locationMatch.Groups["column"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int column)
            && relativeLine > 0
            && column > 0
            && TryGetTrimmedLine(selection.Text, relativeLine, out int lineStart, out int lineLength))
        {
            int documentStart = selection.Start + lineStart;
            int documentLine = CountLinesBefore(documentText, selection.Start) + relativeLine;
            return new KustoQueryErrorHighlight(documentStart, lineLength, documentLine, column);
        }

        return IsSourceError(message)
            ? CreateStatementHighlight(selection, documentText)
            : null;
    }

    private static KustoQueryErrorHighlight? CreateStatementHighlight(
        KustoQuerySelection selection,
        string documentText)
    {
        int leadingWhitespace = 0;
        int trailingWhitespace = selection.Text.Length;

        while (leadingWhitespace < trailingWhitespace && char.IsWhiteSpace(selection.Text[leadingWhitespace]))
        {
            leadingWhitespace++;
        }

        while (trailingWhitespace > leadingWhitespace && char.IsWhiteSpace(selection.Text[trailingWhitespace - 1]))
        {
            trailingWhitespace--;
        }

        if (leadingWhitespace == trailingWhitespace)
        {
            return null;
        }

        int start = selection.Start + leadingWhitespace;
        int lineNumber = CountLinesBefore(documentText, start) + 1;
        return new KustoQueryErrorHighlight(
            start,
            trailingWhitespace - leadingWhitespace,
            lineNumber,
            null);
    }

    private static int CountLinesBefore(string text, int offset)
    {
        int lineBreakCount = 0;

        for (int index = 0; index < Math.Min(offset, text.Length); index++)
        {
            if (text[index] == '\n')
            {
                lineBreakCount++;
            }
        }

        return lineBreakCount;
    }

    private static bool IsSourceError(string message)
    {
        return message.Contains("syntax error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("semantic error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("failed to resolve", StringComparison.OrdinalIgnoreCase)
            || SourceErrorCodeRegex().IsMatch(message);
    }

    private static bool IsValidSelection(KustoQuerySelection selection, string documentText)
    {
        return selection.Start <= documentText.Length
            && selection.Length <= documentText.Length - selection.Start;
    }

    private static bool TryGetTrimmedLine(
        string text,
        int lineNumber,
        out int start,
        out int length)
    {
        int lineStart = 0;

        for (int currentLine = 1; currentLine < lineNumber; currentLine++)
        {
            int newline = text.IndexOf('\n', lineStart);
            if (newline < 0)
            {
                start = 0;
                length = 0;
                return false;
            }

            lineStart = newline + 1;
        }

        int lineEnd = text.IndexOf('\n', lineStart);
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;

        if (lineEnd > lineStart && text[lineEnd - 1] == '\r')
        {
            lineEnd--;
        }

        while (lineStart < lineEnd && char.IsWhiteSpace(text[lineStart]))
        {
            lineStart++;
        }

        while (lineEnd > lineStart && char.IsWhiteSpace(text[lineEnd - 1]))
        {
            lineEnd--;
        }

        start = lineStart;
        length = lineEnd - lineStart;
        return length > 0;
    }

    [GeneratedRegex(
        @"(?:\[\s*line\s*:\s*position\s*=\s*|\bline\s*(?:=|:)?\s*)(?<line>\d+)(?:\s*:\s*|\s*[,;]\s*(?:position|pos|column|col)\s*(?:=|:)?\s*)(?<column>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex LinePositionRegex();

    [GeneratedRegex(
        @"\b(?:SYN|SEM)\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex SourceErrorCodeRegex();
}
