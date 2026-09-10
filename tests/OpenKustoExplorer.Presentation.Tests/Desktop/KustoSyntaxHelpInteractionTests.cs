using Avalonia.Input;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Desktop.Editor;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies timing and range rules shared by KQL hover and explicit help.
/// </summary>
public sealed class KustoSyntaxHelpInteractionTests
{
    /// <summary>
    /// Verifies passive help waits for the requested one-second hover dwell.
    /// </summary>
    [Fact]
    public void HoverHelpUsesOneSecondDelay()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), KustoSyntaxHelpInteraction.HoverDelay);
    }

    /// <summary>
    /// Verifies pointer hover excludes a token's trailing boundary while caret help includes it.
    /// </summary>
    [Fact]
    public void SyntaxRangeUsesInteractionSpecificEndBoundary()
    {
        KustoSyntaxHelp help = new(
            "where",
            "Query operator",
            "T | where Predicate",
            "Filters rows.",
            10,
            5);

        Assert.True(KustoSyntaxHelpInteraction.ContainsPosition(help, 10, includeEnd: false));
        Assert.True(KustoSyntaxHelpInteraction.ContainsPosition(help, 14, includeEnd: false));
        Assert.False(KustoSyntaxHelpInteraction.ContainsPosition(help, 15, includeEnd: false));
        Assert.True(KustoSyntaxHelpInteraction.ContainsPosition(help, 15, includeEnd: true));
    }

    /// <summary>
    /// Verifies repeated F1 requests do not dismiss explicit syntax help.
    /// </summary>
    [Fact]
    public void F1RequestsHelpAndOnlyEscapeDismissesIt()
    {
        Assert.True(KustoSyntaxHelpInteraction.IsRequestKey(Key.F1, KeyModifiers.None));
        Assert.False(KustoSyntaxHelpInteraction.IsRequestKey(Key.F1, KeyModifiers.Control));
        Assert.False(KustoSyntaxHelpInteraction.IsRequestKey(Key.Escape, KeyModifiers.None));
        Assert.True(KustoSyntaxHelpInteraction.IsDismissKey(Key.Escape, KeyModifiers.None));
        Assert.False(KustoSyntaxHelpInteraction.IsDismissKey(Key.F1, KeyModifiers.None));
        Assert.False(KustoSyntaxHelpInteraction.IsDismissKey(Key.Escape, KeyModifiers.Control));
    }
}
