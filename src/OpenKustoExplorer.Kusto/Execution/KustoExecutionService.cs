using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Kusto.Connections;

namespace OpenKustoExplorer.Kusto.Execution;

/// <summary>
/// Executes token-authenticated Kusto operations through the documented REST API.
/// </summary>
public sealed class KustoExecutionService : IKustoCatalogService, IKustoGraphQueryService, IKustoQueryService
{
    /// <summary>
    /// Gets the maximum number of graph rows accepted from Kusto.
    /// </summary>
    public const int MaximumGraphResultRowCount = 2_000_000;

    /// <summary>
    /// Gets the maximum number of rows materialized for ordinary results.
    /// </summary>
    public const int MaximumResultRowCount = 10_000;

    /// <summary>
    /// Gets the maximum number of bytes buffered for a tabular REST response.
    /// </summary>
    public const int MaximumRestResponseByteCount = 67_108_864;

    private readonly IKustoAccessTokenProvider accessTokenProvider;
    private readonly IKustoEndpointPolicy endpointPolicy;
    private readonly HttpClient httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoExecutionService"/> class.
    /// </summary>
    /// <param name="httpClient">The client used for Kusto REST requests.</param>
    /// <param name="accessTokenProvider">The host-specific delegated access-token provider.</param>
    /// <param name="endpointPolicy">The host-specific cluster destination policy.</param>
    public KustoExecutionService(
        HttpClient httpClient,
        IKustoAccessTokenProvider accessTokenProvider,
        IKustoEndpointPolicy endpointPolicy)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(accessTokenProvider);
        ArgumentNullException.ThrowIfNull(endpointPolicy);

        this.httpClient = httpClient;
        this.accessTokenProvider = accessTokenProvider;
        this.endpointPolicy = endpointPolicy;
    }

    /// <inheritdoc />
    public async Task<KustoQueryResult> ExecuteAsync(
        KustoQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Uri clusterUri = await endpointPolicy
            .ValidateAsync(request.ClusterUri, cancellationToken)
            .ConfigureAwait(false);
        bool isManagementCommand = IsManagementCommand(request.QueryText);
        return await ExecuteRestAsync(
            clusterUri,
            isManagementCommand ? "/v1/rest/mgmt" : "/v1/rest/query",
            request.DatabaseName,
            request.QueryText,
            isManagementCommand ? "Command" : "Query",
            MaximumResultRowCount,
            cancellationToken).ConfigureAwait(false);
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
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(request.QueryText.Trim(), plan.Selection.Text, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The graph export plan does not describe the requested query.",
                nameof(plan));
        }

        Uri clusterUri = await endpointPolicy
            .ValidateAsync(request.ClusterUri, cancellationToken)
            .ConfigureAwait(false);
        string accessToken = await GetAccessTokenAsync(clusterUri, cancellationToken).ConfigureAwait(false);
        string clientRequestId = $"OpenKustoExplorer.Graph;{Guid.NewGuid():D}";
        using HttpRequestMessage requestMessage = CreateRestRequest(
            clusterUri,
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
            string responseContent = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
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
        Uri validatedClusterUri = await endpointPolicy
            .ValidateAsync(clusterUri, cancellationToken)
            .ConfigureAwait(false);
        KustoQueryResult result = await ExecuteRestAsync(
            validatedClusterUri,
            "/v1/rest/mgmt",
            string.Empty,
            ".show databases",
            "Catalog",
            MaximumResultRowCount,
            cancellationToken).ConfigureAwait(false);

        return CreateDatabaseInfos(result.Tables);
    }

    /// <inheritdoc />
    public async Task<KustoDatabaseSchema> GetDatabaseSchemaAsync(
        Uri clusterUri,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        Uri validatedClusterUri = await endpointPolicy
            .ValidateAsync(clusterUri, cancellationToken)
            .ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        string escapedDatabaseName = EscapeEntityName(databaseName);
        string command = $".show database {escapedDatabaseName} schema as json";
        KustoQueryResult result = await ExecuteRestAsync(
            validatedClusterUri,
            "/v1/rest/mgmt",
            databaseName,
            command,
            "Schema",
            MaximumResultRowCount,
            cancellationToken).ConfigureAwait(false);
        string schemaJson = FindSchemaJson(result.Tables);
        KustoQueryResult functionResult = await ExecuteRestAsync(
            validatedClusterUri,
            "/v1/rest/mgmt",
            databaseName,
            ".show functions",
            "Functions",
            MaximumResultRowCount,
            cancellationToken).ConfigureAwait(false);
        ReadOnlyCollection<KustoFunctionSchema> functions = KustoFunctionResultParser.Parse(
            functionResult.Tables);
        KustoDatabaseSchema schema = KustoDatabaseSchemaParser.Parse(
            validatedClusterUri.Host,
            databaseName,
            schemaJson);

        return new KustoDatabaseSchema(schema.ClusterName, schema.DatabaseName, schema.Tables, functions);
    }

    /// <summary>
    /// Determines whether query text represents a dot-prefixed Kusto management command.
    /// </summary>
    /// <param name="queryText">The KQL text to classify.</param>
    /// <returns><see langword="true"/> when the first non-whitespace character is a dot.</returns>
    internal static bool IsManagementCommand(string queryText)
    {
        ReadOnlySpan<char> trimmedQuery = queryText.AsSpan().TrimStart();
        return !trimmedQuery.IsEmpty && trimmedQuery[0] == '.';
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

    private static async Task<string> ReadBoundedResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumRestResponseByteCount)
        {
            throw new InvalidDataException("The Kusto response exceeds the configured size limit.");
        }

        await using Stream responseStream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using MemoryStream responseBuffer = new();
        byte[] readBuffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await responseStream
            .ReadAsync(readBuffer.AsMemory(), cancellationToken)
            .ConfigureAwait(false)) > 0)
        {
            if (responseBuffer.Length + bytesRead > MaximumRestResponseByteCount)
            {
                throw new InvalidDataException("The Kusto response exceeds the configured size limit.");
            }

            await responseBuffer.WriteAsync(
                readBuffer.AsMemory(0, bytesRead),
                cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(responseBuffer.GetBuffer(), 0, checked((int)responseBuffer.Length));
    }

    private async Task<KustoQueryResult> ExecuteRestAsync(
        Uri clusterUri,
        string requestPath,
        string databaseName,
        string commandText,
        string requestCategory,
        int maximumRowCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string accessToken = await GetAccessTokenAsync(clusterUri, cancellationToken).ConfigureAwait(false);
        string clientRequestId = $"OpenKustoExplorer.{requestCategory};{Guid.NewGuid():D}";
        using HttpRequestMessage requestMessage = CreateRestRequest(
            clusterUri,
            requestPath,
            databaseName,
            commandText,
            accessToken,
            clientRequestId,
            maximumRowCount);
        Stopwatch stopwatch = Stopwatch.StartNew();
        using HttpResponseMessage response = await httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        string responseContent = await ReadBoundedResponseAsync(response.Content, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            string errorMessage = KustoRestResponseParser.ParseError(
                responseContent,
                $"Kusto returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            throw new HttpRequestException(errorMessage, null, response.StatusCode);
        }

        return KustoRestResponseParser.Parse(responseContent, stopwatch.Elapsed, maximumRowCount);
    }

    private async Task<string> GetAccessTokenAsync(Uri clusterUri, CancellationToken cancellationToken)
    {
        string accessToken = await accessTokenProvider
            .GetAccessTokenAsync(clusterUri, cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(accessToken)
            ? throw new InvalidOperationException("Kusto authentication returned no access token.")
            : accessToken;
    }
}
