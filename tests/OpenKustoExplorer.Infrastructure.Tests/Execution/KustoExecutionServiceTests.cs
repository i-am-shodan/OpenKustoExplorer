using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Kusto.Execution;

namespace OpenKustoExplorer.Infrastructure.Tests.Execution;

/// <summary>
/// Verifies the host-neutral Kusto REST execution contract.
/// </summary>
public sealed class KustoExecutionServiceTests
{
    /// <summary>
    /// Verifies oversized tabular responses are rejected before their bodies are buffered.
    /// </summary>
    /// <returns>A task that completes after the response is rejected.</returns>
    [Fact]
    public async Task ExecuteAsyncRejectsOversizedResponse()
    {
        using CapturingHandler handler = new("{}")
        {
            ResponseContentLength = KustoExecutionService.MaximumRestResponseByteCount + 1L,
        };
        using HttpClient httpClient = new(handler);
        KustoExecutionService service = new(
            httpClient,
            new RecordingTokenProvider(),
            new HttpsKustoEndpointPolicy());

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ExecuteAsync(new KustoQueryRequest(
                new Uri("https://help.kusto.windows.net"),
                "Samples",
                "print Value=42")));

        Assert.Contains("size limit", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies query requests use the canonical cluster, delegated token, and read-only bounds.
    /// </summary>
    /// <returns>A task that completes after the captured request is inspected.</returns>
    [Fact]
    public async Task ExecuteAsyncBuildsBoundedReadOnlyRequest()
    {
        const string Response = """
            {
              "Tables": [{
                "TableName": "PrimaryResult",
                "Columns": [{ "ColumnName": "Value", "ColumnType": "long" }],
                "Rows": [[42]]
              }]
            }
            """;
        using CapturingHandler handler = new(Response);
        using HttpClient httpClient = new(handler);
        RecordingTokenProvider tokenProvider = new();
        KustoExecutionService service = new(
            httpClient,
            tokenProvider,
            new HttpsKustoEndpointPolicy());

        KustoQueryResult result = await service.ExecuteAsync(new KustoQueryRequest(
            new Uri("https://help.kusto.windows.net/some/path"),
            "Samples",
            "print Value=42"));

        Assert.Equal(new Uri("https://help.kusto.windows.net/v1/rest/query"), handler.RequestUri);
        Assert.Equal(new Uri("https://help.kusto.windows.net"), tokenProvider.ClusterUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-token"), handler.Authorization);
        Assert.Equal("true", handler.ReadOnlyHeader);
        Assert.StartsWith("OpenKustoExplorer.Query;", handler.ClientRequestId, StringComparison.Ordinal);
        using JsonDocument requestBody = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal("Samples", requestBody.RootElement.GetProperty("db").GetString());
        Assert.Equal("print Value=42", requestBody.RootElement.GetProperty("csl").GetString());
        JsonElement options = requestBody.RootElement.GetProperty("properties").GetProperty("Options");
        Assert.Equal(KustoExecutionService.MaximumResultRowCount, options.GetProperty("truncationmaxrecords").GetInt32());
        Assert.True(options.GetProperty("request_readonly").GetBoolean());
        Assert.Equal("42", Assert.Single(Assert.Single(result.Tables).Rows).Values[0]);
    }

    /// <summary>
    /// Verifies dot-prefixed commands use the management endpoint and command request category.
    /// </summary>
    /// <returns>A task that completes after the captured request is inspected.</returns>
    [Fact]
    public async Task ExecuteAsyncRoutesManagementCommandToManagementEndpoint()
    {
        const string Response = """
            {
              "Tables": [{
                "TableName": "PrimaryResult",
                "Columns": [{ "ColumnName": "TableName", "ColumnType": "string" }],
                "Rows": [["SyntheticEvents"]]
              }]
            }
            """;
        using CapturingHandler handler = new(Response);
        using HttpClient httpClient = new(handler);
        KustoExecutionService service = new(
            httpClient,
            new RecordingTokenProvider(),
            new HttpsKustoEndpointPolicy());

        KustoQueryResult result = await service.ExecuteAsync(new KustoQueryRequest(
            new Uri("https://help.kusto.windows.net"),
            "Samples",
            " \r\n.show tables"));

        Assert.Equal(new Uri("https://help.kusto.windows.net/v1/rest/mgmt"), handler.RequestUri);
        Assert.StartsWith("OpenKustoExplorer.Command;", handler.ClientRequestId, StringComparison.Ordinal);
        Assert.Equal("SyntheticEvents", Assert.Single(Assert.Single(result.Tables).Rows).Values[0]);
    }

    /// <summary>
    /// Verifies catalog discovery uses the management endpoint and maps database display names once.
    /// </summary>
    /// <returns>A task that completes after the catalog response is mapped.</returns>
    [Fact]
    public async Task GetDatabasesAsyncUsesSharedManagementRequestAndMapping()
    {
        const string Response = """
            {
              "Tables": [{
                "TableName": "PrimaryResult",
                "Columns": [
                  { "ColumnName": "DatabaseName", "ColumnType": "string" },
                  { "ColumnName": "PrettyName", "ColumnType": "string" }
                ],
                "Rows": [["Samples", "Sample data"], ["Samples", "Duplicate"], ["Telemetry", ""]]
              }]
            }
            """;
        using CapturingHandler handler = new(Response);
        using HttpClient httpClient = new(handler);
        KustoExecutionService service = new(
            httpClient,
            new RecordingTokenProvider(),
            new HttpsKustoEndpointPolicy());

        IReadOnlyList<KustoDatabaseInfo> databases = await service.GetDatabasesAsync(
            new Uri("https://help.kusto.windows.net"));

        Assert.Equal(new Uri("https://help.kusto.windows.net/v1/rest/mgmt"), handler.RequestUri);
        using JsonDocument requestBody = JsonDocument.Parse(handler.RequestBody);
        Assert.False(requestBody.RootElement.TryGetProperty("db", out _));
        Assert.Equal(".show databases", requestBody.RootElement.GetProperty("csl").GetString());
        Assert.Equal(2, databases.Count);
        Assert.Equal("Sample data", databases[0].DisplayName);
        Assert.Equal("Telemetry", databases[1].DisplayName);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string response;

        internal CapturingHandler(string response)
        {
            this.response = response;
        }

        internal AuthenticationHeaderValue? Authorization { get; private set; }

        internal string ClientRequestId { get; private set; } = string.Empty;

        internal string ReadOnlyHeader { get; private set; } = string.Empty;

        internal string RequestBody { get; private set; } = string.Empty;

        internal Uri? RequestUri { get; private set; }

        internal long? ResponseContentLength { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            ClientRequestId = request.Headers.GetValues("x-ms-client-request-id").Single();
            ReadOnlyHeader = request.Headers.GetValues("x-ms-readonly").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            StringContent content = new(response, Encoding.UTF8, "application/json");
            content.Headers.ContentLength = ResponseContentLength;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            };
        }
    }

    private sealed class RecordingTokenProvider : IKustoAccessTokenProvider
    {
        internal Uri? ClusterUri { get; private set; }

        public Task<string> GetAccessTokenAsync(
            Uri clusterUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClusterUri = clusterUri;
            return Task.FromResult("test-token");
        }
    }
}
