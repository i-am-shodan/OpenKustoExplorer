using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Domain.Schema;
using static OpenKustoExplorer.Kusto.Gateway.V1.KustoGatewayContracts;

namespace OpenKustoExplorer.Kusto.Gateway.V1;

/// <summary>
/// Calls the same-origin version-one Kusto gateway from a portable client.
/// </summary>
public sealed class KustoGatewayClient : IKustoCatalogService, IKustoGraphQueryService, IKustoQueryService, IDisposable
{
    private static readonly TimeSpan CancellationRequestTimeout = TimeSpan.FromSeconds(5);
    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private KustoGatewaySessionResponse? session;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGatewayClient"/> class.
    /// </summary>
    /// <param name="httpClient">The same-origin HTTP client.</param>
    public KustoGatewayClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<KustoQueryResult> ExecuteAsync(
        KustoQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        Guid operationId = Guid.NewGuid();
        KustoGatewayQueryRequest gatewayRequest = KustoGatewayMapper.ToGatewayRequest(
            operationId,
            request);
        KustoGatewayQueryResponse response = await SendOperationAsync(
            KustoGatewayRoutes.Query,
            operationId,
            gatewayRequest,
            KustoGatewayJsonContext.Default.KustoGatewayQueryRequest,
            KustoGatewayJsonContext.Default.KustoGatewayQueryResponse,
            cancellationToken).ConfigureAwait(false);
        return KustoGatewayMapper.ToDomainResult(response);
    }

    /// <summary>
    /// Gets the Azure OpenAI model configured by the authenticated Web host.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The configured model collection.</returns>
    public async Task<KustoGatewayCopilotModelsResponse> GetCopilotModelsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        KustoGatewayCopilotModelsResponse response = await GetAsync(
            KustoGatewayRoutes.CopilotModels,
            KustoGatewayJsonContext.Default.KustoGatewayCopilotModelsResponse,
            cancellationToken).ConfigureAwait(false);
        EnsureVersion(response.Version);
        return response;
    }

    /// <summary>
    /// Sends one stateless Copilot turn with bounded browser-local history.
    /// </summary>
    /// <param name="context">The current workbench context.</param>
    /// <param name="request">The current natural-language request.</param>
    /// <param name="modelId">The selected server model.</param>
    /// <param name="sharedDataText">The complete consented local snapshot.</param>
    /// <param name="history">Prior browser-local turns.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The assistant reply.</returns>
    public async Task<KustoCopilotReply> SendCopilotAsync(
        KustoCopilotContext context,
        string request,
        string modelId,
        string sharedDataText,
        IReadOnlyList<KustoGatewayCopilotTurn> history,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Guid operationId = Guid.NewGuid();
        KustoGatewayCopilotRequest gatewayRequest = KustoGatewayMapper.ToGatewayCopilotRequest(
            operationId,
            context,
            request,
            modelId,
            sharedDataText,
            history);
        KustoGatewayCopilotResponse response = await SendOperationAsync(
            KustoGatewayRoutes.Copilot,
            operationId,
            gatewayRequest,
            KustoGatewayJsonContext.Default.KustoGatewayCopilotRequest,
            KustoGatewayJsonContext.Default.KustoGatewayCopilotResponse,
            cancellationToken).ConfigureAwait(false);
        return KustoGatewayMapper.ToDomainCopilotReply(response);
    }

    /// <inheritdoc />
    public async Task<KustoGraphExportSummary> ExecuteGraphAsync(
        KustoQueryRequest request,
        KustoGraphQueryPlan plan,
        IKustoGraphExportSink sink,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sink);

        if (!string.Equals(request.QueryText.Trim(), plan.Selection.Text, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The graph export plan does not describe the requested query.",
                nameof(plan));
        }

        Guid operationId = Guid.NewGuid();
        KustoGatewayQueryRequest gatewayRequest = KustoGatewayMapper.ToGatewayRequest(
            operationId,
            request);
        return await SendGraphOperationAsync(
            operationId,
            gatewayRequest,
            sink,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KustoDatabaseInfo>> GetDatabasesAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Guid operationId = Guid.NewGuid();
        KustoGatewayDatabasesRequest request = KustoGatewayMapper.ToGatewayDatabasesRequest(
            operationId,
            clusterUri);
        KustoGatewayDatabasesResponse response = await SendOperationAsync(
            KustoGatewayRoutes.Databases,
            operationId,
            request,
            KustoGatewayJsonContext.Default.KustoGatewayDatabasesRequest,
            KustoGatewayJsonContext.Default.KustoGatewayDatabasesResponse,
            cancellationToken).ConfigureAwait(false);
        return KustoGatewayMapper.ToDomainDatabases(response);
    }

    /// <inheritdoc />
    public async Task<KustoDatabaseSchema> GetDatabaseSchemaAsync(
        Uri clusterUri,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Guid operationId = Guid.NewGuid();
        KustoGatewaySchemaRequest request = KustoGatewayMapper.ToGatewaySchemaRequest(
            operationId,
            clusterUri,
            databaseName);
        KustoGatewaySchemaResponse response = await SendOperationAsync(
            KustoGatewayRoutes.Schema,
            operationId,
            request,
            KustoGatewayJsonContext.Default.KustoGatewaySchemaRequest,
            KustoGatewayJsonContext.Default.KustoGatewaySchemaResponse,
            cancellationToken).ConfigureAwait(false);
        return KustoGatewayMapper.ToDomainSchema(response);
    }

    /// <summary>
    /// Gets the current authenticated gateway session and antiforgery token.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The authenticated gateway session.</returns>
    public async Task<KustoGatewaySessionResponse> GetSessionAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (session is not null)
        {
            return session;
        }

        await sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            session ??= await GetAsync(
                KustoGatewayRoutes.Session,
                KustoGatewayJsonContext.Default.KustoGatewaySessionResponse,
                cancellationToken).ConfigureAwait(false);
            EnsureVersion(session.Version);
            if (string.IsNullOrWhiteSpace(session.AntiforgeryToken))
            {
                throw new InvalidDataException("The gateway returned no antiforgery token.");
            }

            if (string.IsNullOrWhiteSpace(session.StoragePartition))
            {
                throw new InvalidDataException("The gateway returned no browser-storage partition.");
            }

            return session;
        }
        finally
        {
            sessionGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        sessionGate.Dispose();
        disposed = true;
    }

    private static ByteArrayContent CreateJsonContent<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        byte[] contentBytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        if (contentBytes.Length > KustoGatewayRoutes.MaximumRequestByteCount)
        {
            throw new InvalidOperationException("The gateway request exceeds the configured size limit.");
        }

        ByteArrayContent content = new(contentBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8",
        };
        return content;
    }

    private static void EnsureVersion(int version)
    {
        if (version != KustoGatewayRoutes.Version)
        {
            throw new InvalidDataException($"Gateway protocol version {version} is not supported.");
        }
    }

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > KustoGatewayRoutes.MaximumResponseByteCount)
        {
            throw new InvalidDataException("The gateway response exceeds the configured size limit.");
        }

        await using Stream contentStream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using MemoryStream buffer = new();
        byte[] readBuffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await contentStream
            .ReadAsync(readBuffer.AsMemory(), cancellationToken)
            .ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + bytesRead > KustoGatewayRoutes.MaximumResponseByteCount)
            {
                throw new InvalidDataException("The gateway response exceeds the configured size limit.");
            }

            await buffer.WriteAsync(
                readBuffer.AsMemory(0, bytesRead),
                cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private static async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        JsonTypeInfo<T> responseTypeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        byte[] responseBytes = await ReadBoundedContentAsync(response.Content, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message = GetErrorMessage(response, responseBytes);
            throw new HttpRequestException(message, null, response.StatusCode);
        }

        return JsonSerializer.Deserialize(responseBytes, responseTypeInfo)
            ?? throw new InvalidDataException("The Kusto gateway returned an empty JSON response.");
    }

    private static string GetErrorMessage(HttpResponseMessage response, byte[] responseBytes)
    {
        string fallbackMessage = $"The Kusto gateway returned {(int)response.StatusCode} {response.ReasonPhrase}.";
        try
        {
            KustoGatewayErrorResponse? error = JsonSerializer.Deserialize(
                responseBytes,
                KustoGatewayJsonContext.Default.KustoGatewayErrorResponse);
            return string.IsNullOrWhiteSpace(error?.Message) ? fallbackMessage : error.Message;
        }
        catch (JsonException)
        {
            return fallbackMessage;
        }
    }

    private static async Task<KustoGraphExportSummary> ReadGraphResponseAsync(
        HttpResponseMessage response,
        IKustoGraphExportSink sink,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            byte[] responseBytes = await ReadBoundedContentAsync(response.Content, cancellationToken)
                .ConfigureAwait(false);
            string message = GetErrorMessage(response, responseBytes);
            throw new HttpRequestException(message, null, response.StatusCode);
        }

        await using Stream contentStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        KustoGraphExportSummary? summary = null;
        await foreach (KustoGatewayGraphFrame? frame in JsonSerializer.DeserializeAsyncEnumerable(
            contentStream,
            KustoGatewayJsonContext.Default.KustoGatewayGraphFrame,
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (frame is null)
            {
                throw new InvalidDataException("The Kusto gateway returned a null graph frame.");
            }

            EnsureVersion(frame.Version);
            if (summary is not null)
            {
                throw new InvalidDataException("The Kusto gateway returned data after the graph summary.");
            }

            switch (frame.FrameKind)
            {
                case KustoGatewayGraphFrameKind.Batch:
                    KustoGraphExportBatch batch = KustoGatewayMapper.ToDomainGraphBatch(frame);
                    await sink.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                    break;
                case KustoGatewayGraphFrameKind.Summary:
                    summary = KustoGatewayMapper.ToDomainGraphSummary(frame);
                    break;
                case KustoGatewayGraphFrameKind.Error:
                    throw new HttpRequestException(
                        string.IsNullOrWhiteSpace(frame.ErrorMessage)
                            ? "The Kusto gateway graph export failed."
                            : frame.ErrorMessage);
                default:
                    throw new InvalidDataException("The Kusto gateway returned an unknown graph frame kind.");
            }
        }

        return summary
            ?? throw new InvalidDataException("The Kusto gateway returned no graph export summary.");
    }

    private async Task<T> GetAsync<T>(
        string route,
        JsonTypeInfo<T> responseTypeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        using HttpRequestMessage request = new(HttpMethod.Get, route);
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        return await ReadResponseAsync(response, responseTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    private async Task<KustoGraphExportSummary> SendGraphOperationAsync(
        Guid operationId,
        KustoGatewayQueryRequest value,
        IKustoGraphExportSink sink,
        CancellationToken cancellationToken)
    {
        KustoGatewaySessionResponse currentSession = await GetSessionAsync(cancellationToken)
            .ConfigureAwait(false);
        using HttpRequestMessage request = new(HttpMethod.Post, KustoGatewayRoutes.Graph)
        {
            Content = CreateJsonContent(
                value,
                KustoGatewayJsonContext.Default.KustoGatewayQueryRequest),
        };
        request.Headers.Add(KustoGatewayRoutes.AntiforgeryHeaderName, currentSession.AntiforgeryToken);

        try
        {
            using HttpResponseMessage response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return await ReadGraphResponseAsync(response, sink, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryCancelOperationAsync(operationId, currentSession.AntiforgeryToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<TResponse> SendOperationAsync<TRequest, TResponse>(
        string route,
        Guid operationId,
        TRequest value,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        KustoGatewaySessionResponse currentSession = await GetSessionAsync(cancellationToken)
            .ConfigureAwait(false);
        using HttpRequestMessage request = new(HttpMethod.Post, route)
        {
            Content = CreateJsonContent(value, requestTypeInfo),
        };
        request.Headers.Add(KustoGatewayRoutes.AntiforgeryHeaderName, currentSession.AntiforgeryToken);

        try
        {
            using HttpResponseMessage response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return await ReadResponseAsync(response, responseTypeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryCancelOperationAsync(operationId, currentSession.AntiforgeryToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task TryCancelOperationAsync(Guid operationId, string antiforgeryToken)
    {
        using CancellationTokenSource timeout = new(CancellationRequestTimeout);
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            $"{KustoGatewayRoutes.Operations}/{operationId:D}");
        request.Headers.Add(KustoGatewayRoutes.AntiforgeryHeaderName, antiforgeryToken);

        try
        {
            using HttpResponseMessage response = await httpClient
                .SendAsync(request, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }
}
