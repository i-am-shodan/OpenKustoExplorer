using OpenKustoExplorer.Web.Kusto;

namespace OpenKustoExplorer.Web.Authentication;

/// <summary>
/// Clears process-local public-client accounts for local development.
/// </summary>
internal sealed class LocalDevelopmentWebSignOutService : IWebSignOutService
{
    private readonly LocalDevelopmentKustoAccessTokenProvider tokenProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalDevelopmentWebSignOutService"/> class.
    /// </summary>
    /// <param name="tokenProvider">The process-local token provider.</param>
    public LocalDevelopmentWebSignOutService(
        LocalDevelopmentKustoAccessTokenProvider tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        this.tokenProvider = tokenProvider;
    }

    /// <inheritdoc />
    public async Task<IResult> SignOutAsync(CancellationToken cancellationToken = default)
    {
        await tokenProvider.SignOutAsync(cancellationToken).ConfigureAwait(false);
        return Results.Redirect("/");
    }
}
