using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using OpenKustoExplorer.Kusto.Execution;

namespace OpenKustoExplorer.Web.Kusto;

/// <summary>
/// Acquires delegated Azure Data Explorer tokens for the current Web user.
/// </summary>
internal sealed class WebKustoAccessTokenProvider : IKustoAccessTokenProvider
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ITokenAcquisition tokenAcquisition;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebKustoAccessTokenProvider"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">Provides the current authenticated user.</param>
    /// <param name="tokenAcquisition">Acquires delegated access tokens.</param>
    public WebKustoAccessTokenProvider(
        IHttpContextAccessor httpContextAccessor,
        ITokenAcquisition tokenAcquisition)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(tokenAcquisition);

        this.httpContextAccessor = httpContextAccessor;
        this.tokenAcquisition = tokenAcquisition;
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clusterUri);

        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("An authenticated Web user is required.");
        }

        string scope = $"{clusterUri.GetLeftPart(UriPartial.Authority)}/.default";
        return await tokenAcquisition.GetAccessTokenForUserAsync(
            [scope],
            authenticationScheme: OpenIdConnectDefaults.AuthenticationScheme,
            user: httpContext.User,
            tokenAcquisitionOptions: new TokenAcquisitionOptions
            {
                CancellationToken = cancellationToken,
            }).ConfigureAwait(false);
    }
}
