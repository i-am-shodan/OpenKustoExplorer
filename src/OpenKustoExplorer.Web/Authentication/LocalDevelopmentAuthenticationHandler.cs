using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace OpenKustoExplorer.Web.Authentication;

/// <summary>
/// Authenticates the interactive workstation user for loopback development requests only.
/// </summary>
internal sealed class LocalDevelopmentAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Gets the local authentication scheme name.
    /// </summary>
    internal const string SchemeName = "LocalDevelopment";

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalDevelopmentAuthenticationHandler"/> class.
    /// </summary>
    /// <param name="options">The authentication scheme options.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    public LocalDevelopmentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        IPAddress? remoteAddress = Context.Connection.RemoteIpAddress;
        if (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                "Local development authentication accepts loopback requests only."));
        }

        string accountName = Environment.UserName;
        string accountId = $"{Environment.MachineName}\\{accountName}";
        Claim[] claims =
        [
            new Claim(ClaimTypes.NameIdentifier, accountId),
            new Claim(ClaimTypes.Name, accountName),
            new Claim("preferred_username", accountName),
        ];
        ClaimsIdentity identity = new(claims, SchemeName);
        AuthenticationTicket ticket = new(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
