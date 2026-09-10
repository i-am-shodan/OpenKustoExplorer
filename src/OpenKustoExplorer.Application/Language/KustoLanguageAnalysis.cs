namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Contains editor intelligence produced for one immutable KQL document snapshot.
/// </summary>
public sealed class KustoLanguageAnalysis
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoLanguageAnalysis"/> class.
    /// </summary>
    /// <param name="classifications">The classified source ranges.</param>
    /// <param name="completions">The completions available at the requested caret position.</param>
    /// <param name="diagnostics">The syntax and semantic diagnostics.</param>
    /// <param name="completionEditStart">The zero-based start of text replaced by a completion.</param>
    /// <param name="completionEditLength">The number of source characters replaced by a completion.</param>
    /// <param name="syntaxHelp">The contextual help at the analyzed caret position.</param>
    public KustoLanguageAnalysis(
        IEnumerable<KustoClassification> classifications,
        IEnumerable<KustoCompletion> completions,
        IEnumerable<KustoDiagnostic> diagnostics,
        int completionEditStart,
        int completionEditLength,
        KustoSyntaxHelp? syntaxHelp = null)
    {
        Classifications = Array.AsReadOnly(classifications.ToArray());
        Completions = Array.AsReadOnly(completions.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        CompletionEditStart = completionEditStart;
        CompletionEditLength = completionEditLength;
        SyntaxHelp = syntaxHelp;
    }

    /// <summary>
    /// Gets the classified source ranges.
    /// </summary>
    public IReadOnlyList<KustoClassification> Classifications { get; }

    /// <summary>
    /// Gets the completions available at the requested caret position.
    /// </summary>
    public IReadOnlyList<KustoCompletion> Completions { get; }

    /// <summary>
    /// Gets the syntax and semantic diagnostics.
    /// </summary>
    public IReadOnlyList<KustoDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets the zero-based start of text replaced by a completion.
    /// </summary>
    public int CompletionEditStart { get; }

    /// <summary>
    /// Gets the number of source characters replaced by a completion.
    /// </summary>
    public int CompletionEditLength { get; }

    /// <summary>
    /// Gets contextual help for the syntax at the analyzed caret position, when available.
    /// </summary>
    public KustoSyntaxHelp? SyntaxHelp { get; }
}
