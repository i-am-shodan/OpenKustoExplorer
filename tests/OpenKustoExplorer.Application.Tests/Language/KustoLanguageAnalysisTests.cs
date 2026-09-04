using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Application.Tests.Language;

/// <summary>
/// Verifies the immutable application contract returned by KQL language analysis.
/// </summary>
public sealed class KustoLanguageAnalysisTests
{
    /// <summary>
    /// Verifies that analysis snapshots the supplied result collections.
    /// </summary>
    [Fact]
    public void ConstructorCopiesAnalysisCollections()
    {
        KustoClassification originalClassification = new("Table", 0, 11);
        KustoCompletion originalCompletion = new("Table", "StormEvents", "StormEvents", string.Empty);
        KustoDiagnostic originalDiagnostic = new("KS001", "Error", "Example", 0, 1);
        KustoClassification[] mutableClassifications = [originalClassification];
        KustoCompletion[] mutableCompletions = [originalCompletion];
        KustoDiagnostic[] mutableDiagnostics = [originalDiagnostic];

        KustoLanguageAnalysis analysis = new(
            mutableClassifications,
            mutableCompletions,
            mutableDiagnostics,
            2,
            3);
        mutableClassifications[0] = new KustoClassification("PlainText", 0, 1);
        mutableCompletions[0] = new KustoCompletion("Keyword", "where", "where", string.Empty);
        mutableDiagnostics[0] = new KustoDiagnostic("KS002", "Warning", "Replacement", 1, 1);

        Assert.Same(originalClassification, Assert.Single(analysis.Classifications));
        Assert.Same(originalCompletion, Assert.Single(analysis.Completions));
        Assert.Same(originalDiagnostic, Assert.Single(analysis.Diagnostics));
        Assert.Equal(2, analysis.CompletionEditStart);
        Assert.Equal(3, analysis.CompletionEditLength);
    }

    /// <summary>
    /// Verifies that completion insertion preserves text on both sides of the resulting caret.
    /// </summary>
    [Fact]
    public void CompletionPreservesSplitInsertionText()
    {
        KustoCompletion completion = new("Function", "ago()", "ago(", ")");

        Assert.Equal("ago(", completion.BeforeText);
        Assert.Equal(")", completion.AfterText);
    }
}
