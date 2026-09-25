using OpenKustoExplorer.Kusto.Gateway.V1;

namespace OpenKustoExplorer.Web.Tests.Kusto;

/// <summary>
/// Verifies gateway routes remain relative to the Web host's application path.
/// </summary>
public sealed class KustoGatewayRoutesTests
{
    /// <summary>
    /// Verifies the standalone browser entry resolves gateway requests at the origin root.
    /// </summary>
    [Fact]
    public void GetApplicationBaseUriUsesOriginForStandaloneHost()
    {
        Uri result = KustoGatewayRoutes.GetApplicationBaseUri(
            new Uri("https://example.test/app/index.html?fixture=performance"));

        Assert.Equal(new Uri("https://example.test/"), result);
        Assert.Equal(
            new Uri("https://example.test/api/v1/kusto/session"),
            new Uri(result, KustoGatewayRoutes.Session));
    }

    /// <summary>
    /// Verifies a prefixed browser entry keeps gateway requests under the host path base.
    /// </summary>
    [Fact]
    public void GetApplicationBaseUriPreservesHostPathBase()
    {
        Uri result = KustoGatewayRoutes.GetApplicationBaseUri(
            new Uri("https://example.test/oke/app/index.html?fixture=performance#ready"));

        Assert.Equal(new Uri("https://example.test/oke/"), result);
        Assert.Equal(
            new Uri("https://example.test/oke/api/v1/kusto/session"),
            new Uri(result, KustoGatewayRoutes.Session));
    }

    /// <summary>
    /// Verifies unrelated pages cannot silently produce an incorrect gateway base URI.
    /// </summary>
    [Fact]
    public void GetApplicationBaseUriRejectsUnexpectedPagePath()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            KustoGatewayRoutes.GetApplicationBaseUri(new Uri("https://example.test/oke/")));

        Assert.Contains("app/index.html", exception.Message, StringComparison.Ordinal);
    }
}
