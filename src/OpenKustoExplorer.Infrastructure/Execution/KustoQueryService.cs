using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Identity.Client;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Kusto.Authentication;
using OpenKustoExplorer.Kusto.Execution;

namespace OpenKustoExplorer.Infrastructure.Execution;

/// <summary>
/// Executes authenticated Kusto queries through the documented REST API.
/// </summary>
/// <remarks>
/// Cluster metadata supplies Microsoft's public-client identity settings. MSAL performs system-browser
/// authentication, while HTTP and JSON remain explicit to preserve Native AOT compatibility.
/// </remarks>
public sealed class KustoQueryService : IKustoAccessTokenProvider, IKustoCatalogService, IKustoGraphQueryService, IKustoIdentityService, IKustoQueryService, IDisposable
{
    private const int MaximumProfilePhotoSize = 5 * 1024 * 1024;
    private static readonly string[] MicrosoftGraphScopes = ["https://graph.microsoft.com/User.Read"];
    private readonly Dictionary<string, KustoAuthenticationSession> authenticationSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> displayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly KustoExecutionService executionService;
    private readonly HashSet<string> profilePhotoLookups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IPublicClientApplication> publicClientApplications = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, KustoSignedInUser> signedInUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient httpClient;
    private readonly Lock synchronizationRoot = new();
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoQueryService"/> class.
    /// </summary>
    public KustoQueryService()
    {
        SocketsHttpHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        executionService = new KustoExecutionService(
            httpClient,
            this,
            new HttpsKustoEndpointPolicy());
    }

    /// <inheritdoc />
    public event EventHandler? SignedInUsersChanged;

    /// <inheritdoc />
    public Task<KustoQueryResult> ExecuteAsync(
        KustoQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return executionService.ExecuteAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<KustoGraphExportSummary> ExecuteGraphAsync(
        KustoQueryRequest request,
        KustoGraphQueryPlan plan,
        IKustoGraphExportSink sink,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return executionService.ExecuteGraphAsync(request, plan, sink, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KustoDatabaseInfo>> GetDatabasesAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return executionService.GetDatabasesAsync(clusterUri, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OpenKustoExplorer.Domain.Schema.KustoDatabaseSchema> GetDatabaseSchemaAsync(
        Uri clusterUri,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return executionService.GetDatabaseSchemaAsync(clusterUri, databaseName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KustoSignedInUser>> GetSignedInUsersAsync(
        CancellationToken cancellationToken = default)
    {
        List<IPublicClientApplication> applications;

        lock (synchronizationRoot)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            applications = publicClientApplications.Values.Distinct().ToList();
        }

        Dictionary<string, (IPublicClientApplication Application, IAccount Account)> accounts = new(
            StringComparer.OrdinalIgnoreCase);

        foreach (IPublicClientApplication application in applications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<IAccount> applicationAccounts = await application.GetAccountsAsync().ConfigureAwait(false);

            foreach (IAccount account in applicationAccounts)
            {
                accounts.TryAdd(GetAccountId(account), (application, account));
            }
        }

        foreach (KeyValuePair<string, (IPublicClientApplication Application, IAccount Account)> entry in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshSignedInUserAsync(
                entry.Key,
                entry.Value.Application,
                entry.Value.Account,
                cancellationToken).ConfigureAwait(false);
        }

        KustoSignedInUser[] users;

        lock (synchronizationRoot)
        {
            users = signedInUsers.Values
                .Where(user => accounts.ContainsKey(user.AccountId))
                .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(user => user.AccountName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Array.AsReadOnly(users);
    }

    /// <inheritdoc />
    public async Task SignOutAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        List<IPublicClientApplication> applications;

        lock (synchronizationRoot)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            applications = publicClientApplications.Values.Distinct().ToList();
        }

        foreach (IPublicClientApplication application in applications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<IAccount> accounts = await application.GetAccountsAsync().ConfigureAwait(false);

            foreach (IAccount account in accounts.Where(candidate => string.Equals(
                GetAccountId(candidate),
                accountId,
                StringComparison.OrdinalIgnoreCase)))
            {
                await application.RemoveAsync(account).ConfigureAwait(false);
            }
        }

        lock (synchronizationRoot)
        {
            displayNames.Remove(accountId);
            profilePhotoLookups.Remove(accountId);
            signedInUsers.Remove(accountId);
        }

        SignedInUsersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (synchronizationRoot)
        {
            if (!isDisposed)
            {
                isDisposed = true;

                foreach (KustoAuthenticationSession authenticationSession in authenticationSessions.Values)
                {
                    authenticationSession.Dispose();
                }

                authenticationSessions.Clear();
                publicClientApplications.Clear();
                displayNames.Clear();
                profilePhotoLookups.Clear();
                signedInUsers.Clear();
                httpClient.Dispose();
            }
        }
    }

    /// <inheritdoc />
    async Task<string> IKustoAccessTokenProvider.GetAccessTokenAsync(
        Uri clusterUri,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        KustoAuthenticationSession authenticationSession = await GetOrCreateAuthenticationSessionAsync(
            clusterUri,
            cancellationToken).ConfigureAwait(false);
        return await authenticationSession.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string GetAccountId(IAccount account)
    {
        return string.IsNullOrWhiteSpace(account.HomeAccountId?.Identifier)
            ? account.Username
            : account.HomeAccountId.Identifier;
    }

    private async Task RefreshSignedInUserAsync(
        string accountId,
        IPublicClientApplication application,
        IAccount account,
        CancellationToken cancellationToken)
    {
        string accountName = string.IsNullOrWhiteSpace(account.Username) ? accountId : account.Username;
        string displayName;
        byte[] profilePhoto;
        bool lookupProfilePhoto;

        lock (synchronizationRoot)
        {
            displayName = displayNames.GetValueOrDefault(accountId, accountName);
            profilePhoto = signedInUsers.TryGetValue(accountId, out KustoSignedInUser? existingUser)
                ? existingUser.ProfilePhoto.ToArray()
                : [];
            lookupProfilePhoto = profilePhoto.Length == 0 && profilePhotoLookups.Add(accountId);
        }

        if (lookupProfilePhoto)
        {
            try
            {
                profilePhoto = await TryGetProfilePhotoAsync(application, account, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (synchronizationRoot)
                {
                    profilePhotoLookups.Remove(accountId);
                }

                throw;
            }
        }

        KustoSignedInUser user = new(accountId, displayName, accountName, profilePhoto);

        lock (synchronizationRoot)
        {
            signedInUsers[accountId] = user;
        }
    }

    private async Task<byte[]> TryGetProfilePhotoAsync(
        IPublicClientApplication application,
        IAccount account,
        CancellationToken cancellationToken)
    {
        byte[] profilePhoto = [];
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            AuthenticationResult graphAuthentication = await application
                .AcquireTokenSilent(MicrosoftGraphScopes, account)
                .ExecuteAsync(timeoutSource.Token)
                .ConfigureAwait(false);
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                "https://graph.microsoft.com/v1.0/me/photo/$value");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                graphAuthentication.AccessToken);
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);
            long contentLength = response.Content.Headers.ContentLength ?? 0;
            bool isImage = response.Content.Headers.ContentType?.MediaType?.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase) == true;
            bool safeLength = contentLength is >= 0 and <= MaximumProfilePhotoSize;

            if (response.IsSuccessStatusCode && isImage && safeLength)
            {
                byte[] candidate = await response.Content.ReadAsByteArrayAsync(timeoutSource.Token)
                    .ConfigureAwait(false);
                profilePhoto = candidate.Length <= MaximumProfilePhotoSize ? candidate : [];
            }
        }
        catch (MsalException)
        {
            // Profile access is optional and never prompts for additional consent.
        }
        catch (HttpRequestException)
        {
            // An account icon remains available when Microsoft Graph cannot be reached.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Slow profile lookups time out without delaying Kusto work.
        }

        return profilePhoto;
    }

    private void OnAuthenticationCompleted(AuthenticationResult result)
    {
        IAccount? account = result.Account;

        if (account is not null)
        {
            string accountId = GetAccountId(account);
            string accountName = string.IsNullOrWhiteSpace(account.Username) ? accountId : account.Username;
            string displayName = result.ClaimsPrincipal.FindFirst("name")?.Value
                ?? result.ClaimsPrincipal.FindFirst("preferred_username")?.Value
                ?? accountName;

            lock (synchronizationRoot)
            {
                displayNames[accountId] = displayName;
                ReadOnlySpan<byte> profilePhoto = signedInUsers.TryGetValue(
                    accountId,
                    out KustoSignedInUser? existingUser)
                    ? existingUser.ProfilePhoto.Span
                    : [];
                signedInUsers[accountId] = new KustoSignedInUser(
                    accountId,
                    displayName,
                    accountName,
                    profilePhoto);
            }

            SignedInUsersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<KustoAuthenticationSession> GetOrCreateAuthenticationSessionAsync(
        Uri clusterUri,
        CancellationToken cancellationToken)
    {
        string clusterKey = clusterUri.GetLeftPart(UriPartial.Authority);
        KustoAuthenticationSession? authenticationSession;

        lock (synchronizationRoot)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            authenticationSessions.TryGetValue(clusterKey, out authenticationSession);
        }

        if (authenticationSession is null)
        {
            KustoAuthenticationMetadata metadata = await GetAuthenticationMetadataAsync(
                clusterUri,
                cancellationToken).ConfigureAwait(false);
            IPublicClientApplication publicClientApplication = GetOrCreatePublicClientApplication(metadata);
            KustoAuthenticationSession newSession = new(
                metadata,
                publicClientApplication,
                OnAuthenticationCompleted);

            lock (synchronizationRoot)
            {
                ObjectDisposedException.ThrowIf(isDisposed, this);

                if (!authenticationSessions.TryGetValue(clusterKey, out authenticationSession))
                {
                    authenticationSession = newSession;
                    authenticationSessions.Add(clusterKey, authenticationSession);
                    newSession = null!;
                }
            }

            newSession?.Dispose();
        }

        return authenticationSession;
    }

    private IPublicClientApplication GetOrCreatePublicClientApplication(KustoAuthenticationMetadata metadata)
    {
        string applicationKey = string.Join(
            "|",
            metadata.ClientId,
            metadata.AuthorityBaseUrl.TrimEnd('/'),
            metadata.RedirectUri);
        IPublicClientApplication? publicClientApplication;

        lock (synchronizationRoot)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);

            if (!publicClientApplications.TryGetValue(applicationKey, out publicClientApplication))
            {
                publicClientApplication = PublicClientApplicationBuilder
                    .Create(metadata.ClientId)
                    .WithAuthority(metadata.AuthorityBaseUrl, "organizations", true)
                    .WithRedirectUri(metadata.RedirectUri)
                    .Build();
                publicClientApplications.Add(applicationKey, publicClientApplication);
            }
        }

        return publicClientApplication;
    }

    private async Task<KustoAuthenticationMetadata> GetAuthenticationMetadataAsync(
        Uri clusterUri,
        CancellationToken cancellationToken)
    {
        Uri metadataUri = new(clusterUri, "/v1/rest/auth/metadata");
        using HttpRequestMessage request = new(HttpMethod.Get, metadataUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Kusto authentication metadata returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                null,
                response.StatusCode);
        }

        return KustoAuthenticationMetadata.Parse(responseContent);
    }
}
