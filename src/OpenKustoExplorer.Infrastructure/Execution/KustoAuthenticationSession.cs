using Microsoft.Identity.Client;

namespace OpenKustoExplorer.Infrastructure.Execution;

/// <summary>
/// Reuses an MSAL public client and its in-memory token cache for one Kusto cluster.
/// </summary>
internal sealed class KustoAuthenticationSession : IDisposable
{
    private readonly IPublicClientApplication publicClientApplication;
    private readonly Action<AuthenticationResult> authenticationCompleted;
    private readonly SemaphoreSlim tokenGate = new(1, 1);
    private readonly string[] scopes;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAuthenticationSession"/> class.
    /// </summary>
    /// <param name="metadata">The cluster-advertised authentication settings.</param>
    /// <param name="publicClientApplication">The process-lifetime shared MSAL public client.</param>
    /// <param name="authenticationCompleted">Records the successfully authenticated account.</param>
    public KustoAuthenticationSession(
        KustoAuthenticationMetadata metadata,
        IPublicClientApplication publicClientApplication,
        Action<AuthenticationResult> authenticationCompleted)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(publicClientApplication);
        ArgumentNullException.ThrowIfNull(authenticationCompleted);

        this.publicClientApplication = publicClientApplication;
        this.authenticationCompleted = authenticationCompleted;
        scopes = [$"{metadata.ServiceResourceId.TrimEnd('/')}/.default"];
    }

    /// <summary>
    /// Gets a cached access token or starts system-browser authentication when interaction is required.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels authentication.</param>
    /// <returns>A bearer access token for the Kusto service.</returns>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        AuthenticationResult? result;

        try
        {
            result = await TryAcquireTokenSilentlyAsync(cancellationToken).ConfigureAwait(false);
            result ??= await publicClientApplication
                .AcquireTokenInteractive(scopes)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            tokenGate.Release();
        }

        authenticationCompleted(result);
        return result.AccessToken;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        tokenGate.Dispose();
    }

    private async Task<AuthenticationResult?> TryAcquireTokenSilentlyAsync(CancellationToken cancellationToken)
    {
        IEnumerable<IAccount> accounts = await publicClientApplication.GetAccountsAsync().ConfigureAwait(false);
        IAccount? account = accounts.FirstOrDefault();
        AuthenticationResult? result = null;

        if (account is not null)
        {
            try
            {
                result = await publicClientApplication
                    .AcquireTokenSilent(scopes, account)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MsalUiRequiredException)
            {
                // Interactive authentication below refreshes an expired or insufficient session.
            }
        }

        return result;
    }
}
