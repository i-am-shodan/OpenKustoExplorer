using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace OpenKustoExplorer.Web.Authentication;

/// <summary>
/// Signs out the confidential Web session locally and at Microsoft Entra ID.
/// </summary>
internal sealed class FederatedWebSignOutService : IWebSignOutService
{
    /// <inheritdoc />
    public Task<IResult> SignOutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IResult result = Results.SignOut(
            new AuthenticationProperties
            {
                RedirectUri = "/",
            },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        return Task.FromResult(result);
    }
}
