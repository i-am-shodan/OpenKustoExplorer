using Avalonia.Media;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Desktop.Editor;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies syntax-coloring lookup remains bounded for large documents.
/// </summary>
public sealed class KustoSyntaxColorizerTests
{
    /// <summary>
    /// Verifies a line near the end of a large document skips preceding classifications.
    /// </summary>
    [Fact]
    public void LargeDocumentLookupStartsAtVisibleClassification()
    {
        const int ClassificationCount = 6000;
        const int ClassificationStride = 24;
        KustoSyntaxColorizer colorizer = new(_ => Brushes.Black);
        KustoClassification[] classifications = Enumerable.Range(0, ClassificationCount)
            .Reverse()
            .Select(index => new KustoClassification("Keyword", index * ClassificationStride, 8))
            .ToArray();

        colorizer.Update(classifications);

        Assert.Equal(
            ClassificationCount - 1,
            colorizer.FindFirstCandidate((ClassificationCount - 1) * ClassificationStride));
        Assert.Equal(
            ClassificationCount,
            colorizer.FindFirstCandidate(ClassificationCount * ClassificationStride));
    }

    /// <summary>
    /// Verifies lookup retains an earlier classification that spans the visible line.
    /// </summary>
    [Fact]
    public void OverlappingClassificationRemainsCandidate()
    {
        KustoSyntaxColorizer colorizer = new(_ => Brushes.Black);
        colorizer.Update(
        [
            new KustoClassification("Keyword", 40, 4),
            new KustoClassification("Comment", 0, 100),
            new KustoClassification("Column", 20, 4),
        ]);

        Assert.Equal(0, colorizer.FindFirstCandidate(60));
    }
}
