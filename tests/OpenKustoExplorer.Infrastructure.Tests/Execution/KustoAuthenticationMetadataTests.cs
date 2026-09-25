using OpenKustoExplorer.Kusto.Authentication;

namespace OpenKustoExplorer.Infrastructure.Tests.Execution;

/// <summary>
/// Verifies cluster-advertised Kusto interactive authentication settings.
/// </summary>
public sealed class KustoAuthenticationMetadataTests
{
    /// <summary>
    /// Verifies that user authentication uses the multi-tenant login endpoint rather than the first-party tenant URL.
    /// </summary>
    [Fact]
    public void ParseUsesLoginEndpointForInteractiveAuthority()
    {
        const string Metadata = """
            {
              "AzureAD": {
                "LoginEndpoint": "https://login.microsoftonline.com",
                "KustoClientAppId": "db662dc1-0cfe-4e1c-a843-19a68e65be58",
                "KustoClientRedirectUri": "http://localhost",
                "KustoServiceResourceId": "https://kusto.kusto.windows.net",
                "FirstPartyAuthorityUrl": "https://login.microsoftonline.com/first-party-tenant"
              }
            }
            """;

        KustoAuthenticationMetadata metadata = KustoAuthenticationMetadata.Parse(Metadata);

        Assert.Equal("https://login.microsoftonline.com", metadata.AuthorityBaseUrl);
        Assert.Equal("db662dc1-0cfe-4e1c-a843-19a68e65be58", metadata.ClientId);
        Assert.Equal("http://localhost", metadata.RedirectUri);
        Assert.Equal("https://kusto.kusto.windows.net", metadata.ServiceResourceId);
    }
}
