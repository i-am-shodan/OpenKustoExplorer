using Avalonia.Input;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Defines timing and source-range rules shared by KQL syntax-help interactions.
/// </summary>
internal static class KustoSyntaxHelpInteraction
{
    /// <summary>
    /// Gets the dwell time required before passive hover help appears.
    /// </summary>
    internal static TimeSpan HoverDelay { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Determines whether a document position belongs to a syntax-help source range.
    /// </summary>
    /// <param name="help">The syntax help and its source range.</param>
    /// <param name="position">The zero-based document position.</param>
    /// <param name="includeEnd">Whether a caret at the trailing token boundary is included.</param>
    /// <returns><see langword="true"/> when the position belongs to the source range.</returns>
    internal static bool ContainsPosition(KustoSyntaxHelp help, int position, bool includeEnd)
    {
        ArgumentNullException.ThrowIfNull(help);
        int end = help.Start + help.Length;
        return position >= help.Start && (includeEnd ? position <= end : position < end);
    }

    /// <summary>
    /// Determines whether a key press requests explicit syntax help.
    /// </summary>
    /// <param name="key">The pressed key.</param>
    /// <param name="keyModifiers">The active keyboard modifiers.</param>
    /// <returns><see langword="true"/> only for an unmodified F1 key.</returns>
    internal static bool IsRequestKey(Key key, KeyModifiers keyModifiers)
    {
        return key == Key.F1 && keyModifiers == KeyModifiers.None;
    }

    /// <summary>
    /// Determines whether a key press dismisses explicit syntax help.
    /// </summary>
    /// <param name="key">The pressed key.</param>
    /// <param name="keyModifiers">The active keyboard modifiers.</param>
    /// <returns><see langword="true"/> only for an unmodified Escape key.</returns>
    internal static bool IsDismissKey(Key key, KeyModifiers keyModifiers)
    {
        return key == Key.Escape && keyModifiers == KeyModifiers.None;
    }
}
