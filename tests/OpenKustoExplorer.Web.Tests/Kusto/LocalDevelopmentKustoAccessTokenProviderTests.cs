using System.Net;
using Microsoft.AspNetCore.Http;
using OpenKustoExplorer.Web.Kusto;

namespace OpenKustoExplorer.Web.Tests.Kusto;

/// <summary>
/// Verifies the local development token provider's request boundary.
/// </summary>
public sealed class LocalDevelopmentKustoAccessTokenProviderTests
{
    /// <summary>
    /// Verifies remote requests are rejected before any authentication metadata request.
    /// </summary>
    /// <returns>A task that completes after the provider rejects the request.</returns>
    [Fact]
    public async Task GetAccessTokenAsyncRejectsNonLoopbackRequest()
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        using LocalDevelopmentKustoAccessTokenProvider provider = new(
            new HttpContextAccessor { HttpContext = context },
            new HttpClient(new UnexpectedRequestHandler()));

        UnauthorizedAccessException exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => provider.GetAccessTokenAsync(new Uri("https://help.kusto.windows.net")));

        Assert.Contains("loopback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UnexpectedRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("A remote request must fail before metadata lookup.");
        }
    }
}
