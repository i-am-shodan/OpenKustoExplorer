using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Provides schema-aware editor intelligence for immutable KQL document snapshots.
/// </summary>
public interface IKustoLanguageService
{
    /// <summary>
    /// Gets the independent KQL query block selected by a caret position.
    /// </summary>
    /// <param name="text">The complete KQL document text.</param>
    /// <param name="caretPosition">The zero-based caret position.</param>
    /// <returns>The selected executable query, or <see langword="null"/> when the document contains no query.</returns>
    public KustoQuerySelection? GetQueryAtPosition(string text, int caretPosition);

    /// <summary>
    /// Gets a safe node-and-edge export plan when the selected query block's terminal value is a graph.
    /// </summary>
    /// <param name="text">The complete KQL document text.</param>
    /// <param name="caretPosition">The zero-based caret position.</param>
    /// <returns>A validated graph export plan, or <see langword="null"/> for an ordinary tabular query.</returns>
    public KustoGraphQueryPlan? GetGraphQueryPlanAtPosition(string text, int caretPosition);

    /// <summary>
    /// Gets render instructions from the independent KQL query selected by a caret position.
    /// </summary>
    /// <param name="text">The complete KQL document text.</param>
    /// <param name="caretPosition">The zero-based caret position.</param>
    /// <returns>The selected query's render instructions, or <see langword="null"/>.</returns>
    public KustoVisualization? GetVisualizationAtPosition(string text, int caretPosition);

    /// <summary>
    /// Analyzes KQL source text using the supplied active database schema.
    /// </summary>
    /// <param name="text">The complete KQL document text.</param>
    /// <param name="caretPosition">The zero-based caret position used to produce completions.</param>
    /// <param name="databaseSchema">The active cluster and database schema.</param>
    /// <param name="cancellationToken">A token that cancels parsing and semantic analysis.</param>
    /// <returns>Classifications, completions, diagnostics, and completion replacement metadata.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="text"/> or <paramref name="databaseSchema"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="caretPosition"/> is outside the document text.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public KustoLanguageAnalysis Analyze(
        string text,
        int caretPosition,
        KustoDatabaseSchema databaseSchema,
        CancellationToken cancellationToken = default);
}
