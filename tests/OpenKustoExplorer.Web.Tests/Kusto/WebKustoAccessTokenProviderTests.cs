using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Web;
using OpenKustoExplorer.Web.Kusto;

namespace OpenKustoExplorer.Web.Tests.Kusto;

/// <summary>
/// Verifies delegated token acquisition for Web-hosted Kusto requests.
/// </summary>
public sealed class WebKustoAccessTokenProviderTests
{
    /// <summary>
    /// Verifies token acquisition is scoped to the validated cluster and current user.
    /// </summary>
    /// <returns>A task that completes after the token request is inspected.</returns>
    [Fact]
    public async Task GetAccessTokenAsyncUsesClusterDefaultScopeAndCurrentUser()
    {
        ClaimsPrincipal user = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "account")],
            "Test"));
        DefaultHttpContext httpContext = new()
        {
            User = user,
        };
        ITokenAcquisition tokenAcquisition = DispatchProxy
            .Create<ITokenAcquisition, RecordingTokenAcquisitionProxy>();
        RecordingTokenAcquisitionProxy recorder = (RecordingTokenAcquisitionProxy)tokenAcquisition;
        WebKustoAccessTokenProvider provider = new(
            new HttpContextAccessor { HttpContext = httpContext },
            tokenAcquisition);
        using CancellationTokenSource cancellation = new();

        string token = await provider.GetAccessTokenAsync(
            new Uri("https://help.kusto.windows.net"),
            cancellation.Token);

        Assert.Equal("delegated-token", token);
        Assert.Equal(["https://help.kusto.windows.net/.default"], recorder.Scopes);
        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, recorder.AuthenticationScheme);
        Assert.Same(user, recorder.User);
        Assert.Equal(cancellation.Token, recorder.Options?.CancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1852:Seal internal types",
        Justification = "DispatchProxy generates a runtime subtype.")]
    private class RecordingTokenAcquisitionProxy : DispatchProxy
    {
        internal string? AuthenticationScheme { get; private set; }

        internal TokenAcquisitionOptions? Options { get; private set; }

        internal IReadOnlyList<string> Scopes { get; private set; } = [];

        internal ClaimsPrincipal? User { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            ArgumentNullException.ThrowIfNull(args);

            if (targetMethod.Name == nameof(ITokenAcquisition.GetAccessTokenForUserAsync)
                && targetMethod.ReturnType == typeof(Task<string>))
            {
                Scopes = ((IEnumerable<string>)args[0]!).ToArray();
                AuthenticationScheme = args[1] as string;
                User = args.OfType<ClaimsPrincipal>().Single();
                Options = args.OfType<TokenAcquisitionOptions>().Single();
                return Task.FromResult("delegated-token");
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }
}
