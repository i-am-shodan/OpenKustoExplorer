namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Describes a completion proposed at the current caret position.
/// </summary>
public sealed class KustoCompletion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCompletion"/> class.
    /// </summary>
    /// <param name="kind">The stable completion category supplied by the Kusto language service.</param>
    /// <param name="displayText">The text presented in the completion list.</param>
    /// <param name="beforeText">The text inserted before the resulting caret position.</param>
    /// <param name="afterText">The text inserted after the resulting caret position.</param>
    /// <param name="priority">The relative selection priority; larger values are preferred.</param>
    public KustoCompletion(
        string kind,
        string displayText,
        string beforeText,
        string afterText,
        double priority = 0)
    {
        Kind = kind;
        DisplayText = displayText;
        BeforeText = beforeText;
        AfterText = afterText;
        Priority = priority;
    }

    /// <summary>
    /// Gets the stable completion category supplied by the Kusto language service.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the text presented in the completion list.
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Gets the text inserted before the resulting caret position.
    /// </summary>
    public string BeforeText { get; }

    /// <summary>
    /// Gets the text inserted after the resulting caret position.
    /// </summary>
    public string AfterText { get; }

    /// <summary>
    /// Gets the relative selection priority; larger values are preferred.
    /// </summary>
    public double Priority { get; }
}
