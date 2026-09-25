using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenKustoExplorer.Web.Authentication;

namespace OpenKustoExplorer.Web.Tests.Authentication;

/// <summary>
/// Verifies the local development authentication network boundary.
/// </summary>
public sealed class LocalDevelopmentAuthenticationHandlerTests
{
    /// <summary>
    /// Verifies a loopback request receives the process-local development identity.
    /// </summary>
    /// <param name="address">The loopback address.</param>
    /// <returns>A task that completes after authentication.</returns>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task AuthenticateAsyncAcceptsLoopback(string address)
    {
        using ServiceProvider services = CreateServices();
        DefaultHttpContext context = CreateContext(services, IPAddress.Parse(address));

        AuthenticateResult result = await context.AuthenticateAsync(
            LocalDevelopmentAuthenticationHandler.SchemeName);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Principal?.Identity?.Name);
    }

    /// <summary>
    /// Verifies a nonloopback request cannot use local development authentication.
    /// </summary>
    /// <returns>A task that completes after authentication is rejected.</returns>
    [Fact]
    public async Task AuthenticateAsyncRejectsNonloopbackAddress()
    {
        using ServiceProvider services = CreateServices();
        DefaultHttpContext context = CreateContext(services, IPAddress.Parse("203.0.113.10"));

        AuthenticateResult result = await context.AuthenticateAsync(
            LocalDevelopmentAuthenticationHandler.SchemeName);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    private static DefaultHttpContext CreateContext(
        IServiceProvider services,
        IPAddress remoteAddress)
    {
        DefaultHttpContext context = new()
        {
            RequestServices = services,
        };
        context.Connection.RemoteIpAddress = remoteAddress;
        return context;
    }

    private static ServiceProvider CreateServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services
            .AddAuthentication(LocalDevelopmentAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, LocalDevelopmentAuthenticationHandler>(
                LocalDevelopmentAuthenticationHandler.SchemeName,
                _ => { });
        return services.BuildServiceProvider();
    }
}
