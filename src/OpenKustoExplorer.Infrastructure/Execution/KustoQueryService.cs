using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Infrastructure.Connections;

namespace OpenKustoExplorer.Infrastructure.Execution;

/// <summary>
/// Executes authenticated Kusto queries through the documented REST API.
/// </summary>
/// <remarks>
/// Cluster metadata supplies Microsoft's public-client identity settings. MSAL performs system-browser
/// authentication, while HTTP and JSON remain explicit to preserve Native AOT compatibility.
/// </remarks>
public sealed class KustoQueryService : IKustoCatalogService, IKustoGraphQueryService, IKustoIdentityService, IKustoQueryService, IDisposable
{
    private const int MaximumGraphResultRowCount = 2_000_000;
    private const int MaximumProfilePhotoSize = 5 * 1024 * 1024;
    private const int MaximumResultRowCount = 10_000;
    private static readonly string[] MicrosoftGraphScopes = ["https://graph.microsoft.com/User.Read"];
    private readonly Dictionary<string, KustoAuthenticationSession> authenticationSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> displayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> profilePhotoLookups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IPublicClientApplication> publicClientApplications = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, KustoSignedInUser> signedInUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient httpClient;
    private readonly object synchronizationRoot = new();
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
    }

    /// <inheritdoc />
    public event EventHandler? SignedInUsersChanged;

    /// <inheritdoc />
    public async Task<KustoQueryResult> ExecuteAsync(
        KustoQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        KustoQueryResult result = await ExecuteRestAsync(
            request.ClusterUri,
            "/v1/rest/query",
            request.DatabaseName,
            request.QueryText,
            "Query",
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    public async Task<KustoGraphExportSummary> ExecuteGraphAsync(
        KustoQueryRequest request,
        KustoGraphQueryPlan plan,
        IKustoGraphExportSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sink);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(request.QueryText.Trim(), plan.Selection.Text, StringComparison.Ordinal))
        {
            throw new ArgumentException("The graph export plan does not describe the requested query.", nameof(plan));
        }

        KustoAuthenticationSession authenticationSession = await GetOrCreateAuthenticationSessionAsync(
            request.ClusterUri,
            cancellationToken).ConfigureAwait(false);
        string accessToken = await authenticationSession.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        string clientRequestId = $"OpenKustoExplorer.Graph;{Guid.NewGuid():D}";
        using HttpRequestMessage requestMessage = CreateRestRequest(
            request.ClusterUri,
            "/v1/rest/query",
            request.DatabaseName,
            plan.ExportQueryText,
            accessToken,
            clientRequestId,
            MaximumGraphResultRowCount);
        Stopwatch stopwatch = Stopwatch.StartNew();
        using HttpResponseMessage response = await httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string errorMessage = KustoRestResponseParser.ParseError(
                responseContent,
                $"Kusto returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            throw new HttpRequestException(errorMessage, null, response.StatusCode);
        }

        await using Stream responseStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        KustoGraphExportSummary parsedSummary = await KustoGraphRestStreamParser.ParseAsync(
            responseStream,
            plan,
            sink,
            TimeSpan.Zero,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return new KustoGraphExportSummary(
            parsedSummary.NodeCount,
            parsedSummary.EdgeCount,
            stopwatch.Elapsed,
            parsedSummary.ResultTable);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KustoDatabaseInfo>> GetDatabasesAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default)
    {
        ValidateClusterUri(clusterUri);
        KustoQueryResult result = await ExecuteRestAsync(
            clusterUri,
            "/v1/rest/mgmt",
            string.Empty,
            ".show databases",
            "Catalog",
            cancellationToken).ConfigureAwait(false);

        return CreateDatabaseInfos(result.Tables);
    }

    /// <inheritdoc />
    public async Task<KustoDatabaseSchema> GetDatabaseSchemaAsync(
        Uri clusterUri,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        ValidateClusterUri(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        string escapedDatabaseName = EscapeEntityName(databaseName);
        string command = $".show database {escapedDatabaseName} schema as json";
        KustoQueryResult result = await ExecuteRestAsync(
            clusterUri,
            "/v1/rest/mgmt",
            databaseName,
            command,
            "Schema",
            cancellationToken).ConfigureAwait(false);
        string schemaJson = FindSchemaJson(result.Tables);
        KustoQueryResult functionResult = await ExecuteRestAsync(
            clusterUri,
            "/v1/rest/mgmt",
            databaseName,
            ".show functions",
            "Functions",
            cancellationToken).ConfigureAwait(false);
        ReadOnlyCollection<KustoFunctionSchema> functions = KustoFunctionResultParser.Parse(functionResult.Tables);
        KustoDatabaseSchema schema = KustoDatabaseSchemaParser.Parse(clusterUri.Host, databaseName, schemaJson);

        return new KustoDatabaseSchema(schema.ClusterName, schema.DatabaseName, schema.Tables, functions);
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

    private static HttpRequestMessage CreateRestRequest(
        Uri clusterUri,
        string requestPath,
        string databaseName,
        string commandText,
        string accessToken,
        string clientRequestId,
        int maximumRowCount)
    {
        Uri requestUri = new(clusterUri, requestPath);
        HttpRequestMessage requestMessage = new(HttpMethod.Post, requestUri)
        {
            Content = CreateRestContent(databaseName, commandText, maximumRowCount),
        };
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        requestMessage.Headers.Add("x-ms-app", "OpenKustoExplorer");
        requestMessage.Headers.Add("x-ms-client-request-id", clientRequestId);
        requestMessage.Headers.Add("x-ms-readonly", "true");

        return requestMessage;
    }

    private static ByteArrayContent CreateRestContent(
        string databaseName,
        string commandText,
        int maximumRowCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRowCount);
        using MemoryStream contentStream = new();
        using (Utf8JsonWriter writer = new(contentStream))
        {
            writer.WriteStartObject();

            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                writer.WriteString("db", databaseName);
            }

            writer.WriteString("csl", commandText);
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            writer.WritePropertyName("Options");
            writer.WriteStartObject();
            writer.WriteNumber("truncationmaxrecords", maximumRowCount);
            writer.WriteBoolean("request_readonly", true);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        ByteArrayContent content = new(contentStream.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8",
        };

        return content;
    }

    private static ReadOnlyCollection<KustoDatabaseInfo> CreateDatabaseInfos(
        IReadOnlyList<KustoResultTable> tables)
    {
        KustoResultTable? databaseTable = null;
        int databaseNameIndex = -1;
        int prettyNameIndex = -1;

        foreach (KustoResultTable table in tables)
        {
            int candidateIndex = GetColumnIndex(table, "DatabaseName");
            if (candidateIndex >= 0)
            {
                databaseTable = table;
                databaseNameIndex = candidateIndex;
                prettyNameIndex = GetColumnIndex(table, "PrettyName");
            }
        }

        if (databaseTable is null)
        {
            throw new InvalidDataException("Kusto returned no database catalog table.");
        }

        List<KustoDatabaseInfo> databases = [];

        foreach (IReadOnlyList<string> values in databaseTable.Rows
            .Select(row => row.Values)
            .Where(values => !string.IsNullOrWhiteSpace(values[databaseNameIndex]))
            .DistinctBy(values => values[databaseNameIndex], StringComparer.OrdinalIgnoreCase))
        {
            string name = values[databaseNameIndex];
            string prettyName = prettyNameIndex >= 0 ? values[prettyNameIndex] : string.Empty;
            string displayName = string.IsNullOrWhiteSpace(prettyName) ? name : prettyName;
            databases.Add(new KustoDatabaseInfo(name, displayName));
        }

        return databases.AsReadOnly();
    }

    private static string EscapeEntityName(string entityName)
    {
        string escapedName = entityName.Replace("'", "''", StringComparison.Ordinal);
        return $"['{escapedName}']";
    }

    private static string FindSchemaJson(IReadOnlyList<KustoResultTable> tables)
    {
        string? schemaJson = tables
            .SelectMany(table => table.Rows)
            .SelectMany(row => row.Values)
            .LastOrDefault(value => value.StartsWith('{')
                && value.Contains("\"Databases\"", StringComparison.Ordinal));

        if (schemaJson is null)
        {
            throw new InvalidDataException("Kusto returned no database schema JSON.");
        }

        return schemaJson;
    }

    private static int GetColumnIndex(KustoResultTable table, string columnName)
    {
        int columnIndex = -1;

        for (int index = 0; index < table.Columns.Count; index++)
        {
            if (string.Equals(table.Columns[index].Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                columnIndex = index;
            }
        }

        return columnIndex;
    }

    private static string GetAccountId(IAccount account)
    {
        return string.IsNullOrWhiteSpace(account.HomeAccountId?.Identifier)
            ? account.Username
            : account.HomeAccountId.Identifier;
    }

    private static void ValidateClusterUri(Uri clusterUri)
    {
        ArgumentNullException.ThrowIfNull(clusterUri);

        if (!clusterUri.IsAbsoluteUri || clusterUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The cluster URI must be an absolute HTTPS URI.", nameof(clusterUri));
        }
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

    private async Task<KustoQueryResult> ExecuteRestAsync(
        Uri clusterUri,
        string requestPath,
        string databaseName,
        string commandText,
        string requestCategory,
        CancellationToken cancellationToken)
    {
        KustoAuthenticationSession authenticationSession = await GetOrCreateAuthenticationSessionAsync(
            clusterUri,
            cancellationToken).ConfigureAwait(false);
        string accessToken = await authenticationSession.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        string clientRequestId = $"OpenKustoExplorer.{requestCategory};{Guid.NewGuid():D}";
        using HttpRequestMessage requestMessage = CreateRestRequest(
            clusterUri,
            requestPath,
            databaseName,
            commandText,
            accessToken,
            clientRequestId,
            MaximumResultRowCount);
        Stopwatch stopwatch = Stopwatch.StartNew();
        using HttpResponseMessage response = await httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        string responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            string errorMessage = KustoRestResponseParser.ParseError(
                responseContent,
                $"Kusto returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            throw new HttpRequestException(errorMessage, null, response.StatusCode);
        }

        return KustoRestResponseParser.Parse(responseContent, stopwatch.Elapsed, MaximumResultRowCount);
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
