using System.Net.Http.Headers;
using Microsoft.Identity.Client;
using OpenKustoExplorer.Kusto.Authentication;
using OpenKustoExplorer.Kusto.Execution;

namespace OpenKustoExplorer.Web.Kusto;

/// <summary>
/// Acquires user-delegated Kusto tokens through a local system browser without an application credential.
/// </summary>
internal sealed class LocalDevelopmentKustoAccessTokenProvider : IKustoAccessTokenProvider, IDisposable
{
    private const int MaximumMetadataByteCount = 1_048_576;
    private readonly Dictionary<string, AuthenticationSession> authenticationSessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HttpClient httpClient;
    private readonly Dictionary<string, IPublicClientApplication> publicClientApplications =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock synchronizationRoot = new();
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalDevelopmentKustoAccessTokenProvider"/> class.
    /// </summary>
    /// <param name="httpClient">The client used to retrieve Kusto authentication metadata.</param>
    public LocalDevelopmentKustoAccessTokenProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(clusterUri);
        AuthenticationSession session = await GetOrCreateSessionAsync(clusterUri, cancellationToken)
            .ConfigureAwait(false);
        return await session.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes all accounts from the process-local MSAL caches.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels account discovery.</param>
    /// <returns>A task that completes after all accounts are removed.</returns>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        List<IPublicClientApplication> applications;
        lock (synchronizationRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            applications = publicClientApplications.Values.Distinct().ToList();
        }

        foreach (IPublicClientApplication application in applications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<IAccount> accounts = await application.GetAccountsAsync().ConfigureAwait(false);
            foreach (IAccount account in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await application.RemoveAsync(account).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (synchronizationRoot)
        {
            if (disposed)
            {
                return;
            }

            foreach (AuthenticationSession session in authenticationSessions.Values)
            {
                session.Dispose();
            }

            authenticationSessions.Clear();
            publicClientApplications.Clear();
            httpClient.Dispose();
            disposed = true;
        }
    }

    private static async Task<string> ReadMetadataAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumMetadataByteCount)
        {
            throw new InvalidDataException("The Kusto authentication metadata exceeds the size limit.");
        }

        await content.LoadIntoBufferAsync(MaximumMetadataByteCount, cancellationToken)
            .ConfigureAwait(false);
        return await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private IPublicClientApplication GetOrCreatePublicClientApplication(
        KustoAuthenticationMetadata metadata)
    {
        string applicationKey = string.Join(
            "|",
            metadata.ClientId,
            metadata.AuthorityBaseUrl.TrimEnd('/'),
            metadata.RedirectUri);

        lock (synchronizationRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!publicClientApplications.TryGetValue(
                applicationKey,
                out IPublicClientApplication? application))
            {
                application = PublicClientApplicationBuilder
                    .Create(metadata.ClientId)
                    .WithAuthority(metadata.AuthorityBaseUrl, "organizations", true)
                    .WithRedirectUri(metadata.RedirectUri)
                    .Build();
                publicClientApplications.Add(applicationKey, application);
            }

            return application;
        }
    }

    private async Task<AuthenticationSession> GetOrCreateSessionAsync(
        Uri clusterUri,
        CancellationToken cancellationToken)
    {
        string clusterKey = clusterUri.GetLeftPart(UriPartial.Authority);
        lock (synchronizationRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (authenticationSessions.TryGetValue(clusterKey, out AuthenticationSession? existingSession))
            {
                return existingSession;
            }
        }

        KustoAuthenticationMetadata metadata = await GetAuthenticationMetadataAsync(
            clusterUri,
            cancellationToken).ConfigureAwait(false);
        AuthenticationSession newSession = new(
            GetOrCreatePublicClientApplication(metadata),
            $"{metadata.ServiceResourceId.TrimEnd('/')}/.default");

        lock (synchronizationRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (authenticationSessions.TryGetValue(clusterKey, out AuthenticationSession? existingSession))
            {
                newSession.Dispose();
                return existingSession;
            }

            authenticationSessions.Add(clusterKey, newSession);
            return newSession;
        }
    }

    private async Task<KustoAuthenticationMetadata> GetAuthenticationMetadataAsync(
        Uri clusterUri,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            new Uri(clusterUri, "/v1/rest/auth/metadata"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Kusto authentication metadata returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                null,
                response.StatusCode);
        }

        string responseContent = await ReadMetadataAsync(response.Content, cancellationToken)
            .ConfigureAwait(false);
        return KustoAuthenticationMetadata.Parse(responseContent);
    }

    private sealed class AuthenticationSession : IDisposable
    {
        private readonly IPublicClientApplication application;
        private readonly string[] scopes;
        private readonly SemaphoreSlim tokenGate = new(1, 1);

        public AuthenticationSession(IPublicClientApplication application, string scope)
        {
            this.application = application;
            scopes = [scope];
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            await tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AuthenticationResult? result = await TryAcquireTokenSilentlyAsync(cancellationToken)
                    .ConfigureAwait(false);
                result ??= await application
                    .AcquireTokenInteractive(scopes)
                    .WithUseEmbeddedWebView(false)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
                return result.AccessToken;
            }
            finally
            {
                tokenGate.Release();
            }
        }

        public void Dispose()
        {
            tokenGate.Dispose();
        }

        private async Task<AuthenticationResult?> TryAcquireTokenSilentlyAsync(
            CancellationToken cancellationToken)
        {
            IAccount? account = (await application.GetAccountsAsync().ConfigureAwait(false))
                .FirstOrDefault();
            if (account is null)
            {
                return null;
            }

            try
            {
                return await application
                    .AcquireTokenSilent(scopes, account)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MsalUiRequiredException)
            {
                return null;
            }
        }
    }
}
