using OpenKustoExplorer.Desktop;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies deferred workbench content follows workflow visibility.
/// </summary>
public sealed class DeferredContentControlTests
{
    /// <summary>
    /// Verifies content is materialized only while requested and follows data-context changes.
    /// </summary>
    [Fact]
    public void MaterializationTracksStateAndDataContext()
    {
        object first = new();
        object second = new();
        DeferredContentControl control = new() { DataContext = first };

        Assert.Null(control.Content);

        control.IsContentMaterialized = true;

        Assert.Same(first, control.Content);

        control.DataContext = second;

        Assert.Same(second, control.Content);

        control.IsContentMaterialized = false;

        Assert.Null(control.Content);
    }

    /// <summary>
    /// Verifies retained content stays alive after its first activation.
    /// </summary>
    [Fact]
    public void RetainedContentSurvivesDeactivation()
    {
        object content = new();
        DeferredContentControl control = new()
        {
            DataContext = content,
            RetainContent = true,
        };

        control.IsContentMaterialized = true;
        control.IsContentMaterialized = false;

        Assert.Same(content, control.Content);
    }
}
