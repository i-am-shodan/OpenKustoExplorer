using Avalonia.Controls;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Desktop.Editor;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies contextual KQL help content for hover and explicit presentations.
/// </summary>
public sealed class KustoSyntaxHelpContentTests
{
    /// <summary>
    /// Verifies expanded F1 help exposes the official documentation destination.
    /// </summary>
    [Fact]
    public void ExpandedHelpIncludesDocumentationLink()
    {
        Uri documentationUri = CreateDocumentationUri();
        KustoSyntaxHelp help = CreateHelp(documentationUri);

        KustoSyntaxHelpContent content = new(help, isCompact: false);

        HyperlinkButton link = Assert.Single(content.Children.OfType<HyperlinkButton>());
        Assert.Equal(documentationUri, link.NavigateUri);
    }

    /// <summary>
    /// Verifies compact hover help remains informational and does not expose a link.
    /// </summary>
    [Fact]
    public void CompactHelpOmitsDocumentationLink()
    {
        KustoSyntaxHelp help = CreateHelp(CreateDocumentationUri());

        KustoSyntaxHelpContent content = new(help, isCompact: true);

        Assert.Empty(content.Children.OfType<HyperlinkButton>());
    }

    private static Uri CreateDocumentationUri()
    {
        return new UriBuilder(Uri.UriSchemeHttps, "learn.microsoft.com")
        {
            Path = "en-us/kusto/query/project-operator",
            Query = "view=microsoft-fabric",
        }.Uri;
    }

    private static KustoSyntaxHelp CreateHelp(Uri documentationUri)
    {
        return new KustoSyntaxHelp(
            "project",
            "Query operator",
            "T | project Expression, ...",
            "Selects output columns.",
            14,
            7,
            documentationUri);
    }
}
