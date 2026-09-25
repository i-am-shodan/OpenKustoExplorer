using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Kusto.Execution;
using OpenKustoExplorer.Kusto.Gateway.V1;
using OpenKustoExplorer.Kusto.Language;
using OpenKustoExplorer.Web.Assistance;
using static OpenKustoExplorer.Kusto.Gateway.V1.KustoGatewayContracts;

namespace OpenKustoExplorer.Web.Tests.Kusto;

/// <summary>
/// Verifies the authenticated same-origin Kusto gateway.
/// </summary>
public sealed class KustoGatewayIntegrationTests
{
    /// <summary>
    /// Verifies the deployed host initializes with a managed-identity federated credential and no secret.
    /// </summary>
    /// <returns>A task that completes after the health endpoint responds.</returns>
    [Fact]
    public async Task FederatedHostInitializesWithoutClientSecret()
    {
        using FederatedWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using HttpResponseMessage response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Verifies browser-local storage is partitioned by a stable opaque account value.
    /// </summary>
    /// <returns>A task that completes after two independent sessions are inspected.</returns>
    [Fact]
    public async Task SessionReturnsStableOpaqueStoragePartition()
    {
        using HttpClient adxClient = new(new CapturingAdxHandler());
        using TestWebApplicationFactory factory = new(adxClient);
        using HttpClient browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using KustoGatewayClient firstClient = new(browserClient);
        using KustoGatewayClient secondClient = new(browserClient);

        KustoGatewayContracts.KustoGatewaySessionResponse first = await firstClient.GetSessionAsync();
        KustoGatewayContracts.KustoGatewaySessionResponse second = await secondClient.GetSessionAsync();

        Assert.Equal(first.StoragePartition, second.StoragePartition);
        Assert.Matches("^[0-9a-f]{64}$", first.StoragePartition);
        Assert.DoesNotContain(first.AccountId, first.StoragePartition, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies database and schema discovery traverse the shared management-query gateway.
    /// </summary>
    /// <returns>A task that completes after catalog and schema results are inspected.</returns>
    [Fact]
    public async Task CatalogAndSchemaUseSharedGateway()
    {
        const string CatalogResponse = """
                        {
                            "Tables": [{
                                "TableName": "PrimaryResult",
                                "Columns": [
                                    { "ColumnName": "DatabaseName", "ColumnType": "string" },
                                    { "ColumnName": "PrettyName", "ColumnType": "string" }
                                ],
                                "Rows": [["Telemetry", "Telemetry data"]]
                            }]
                        }
                        """;
        const string SchemaJson = """
                        {
                            "Databases": {
                                "Telemetry": {
                                    "Name": "Telemetry",
                                    "Tables": {
                                        "Events": {
                                            "Name": "Events",
                                            "OrderedColumns": [
                                                { "Name": "Timestamp", "Type": "System.DateTime" }
                                            ]
                                        }
                                    }
                                }
                            }
                        }
                        """;
        string schemaResponse = JsonSerializer.Serialize(new
        {
            Tables = new[]
            {
                new
                {
                    TableName = "PrimaryResult",
                    Columns = new[] { new { ColumnName = "Schema", ColumnType = "string" } },
                    Rows = new[] { new[] { SchemaJson } },
                },
            },
        });
        const string FunctionsResponse = """
                        {
                            "Tables": [{
                                "TableName": "PrimaryResult",
                                "Columns": [
                                    { "ColumnName": "Name", "ColumnType": "string" },
                                    { "ColumnName": "Parameters", "ColumnType": "string" },
                                    { "ColumnName": "Body", "ColumnType": "string" },
                                    { "ColumnName": "Folder", "ColumnType": "string" },
                                    { "ColumnName": "DocString", "ColumnType": "string" }
                                ],
                                "Rows": [["RecentEvents", "()", "{ Events }", "Samples", "Recent events"]]
                            }]
                        }
                        """;
        using CapturingAdxHandler adxHandler = new(
            CatalogResponse,
            schemaResponse,
            FunctionsResponse);
        using HttpClient adxClient = new(adxHandler);
        using TestWebApplicationFactory factory = new(adxClient);
        using HttpClient browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using KustoGatewayClient gatewayClient = new(browserClient);
        Uri clusterUri = new("https://help.kusto.windows.net");

        IReadOnlyList<KustoDatabaseInfo> databases = await gatewayClient.GetDatabasesAsync(clusterUri);
        KustoDatabaseSchema schema = await gatewayClient.GetDatabaseSchemaAsync(clusterUri, "Telemetry");

        Assert.Equal("Telemetry data", Assert.Single(databases).DisplayName);
        KustoTableSchema table = Assert.Single(schema.Tables);
        Assert.Equal("Events", table.Name);
        Assert.Equal(KustoScalarType.DateTime, Assert.Single(table.Columns).Type);
        Assert.Equal("RecentEvents()", Assert.Single(schema.Functions).Signature);
        Assert.Equal(3, adxHandler.RequestCount);
    }

    /// <summary>
    /// Verifies the portable client reaches the shared Kusto engine through the protected Web gateway.
    /// </summary>
    /// <returns>A task that completes after the returned query result is inspected.</returns>
    [Fact]
    public async Task ExecuteAsyncUsesAuthenticatedAntiforgeryProtectedGateway()
    {
        const string AdxResponse = """
            {
              "Tables": [{
                "TableName": "PrimaryResult",
                "Columns": [{ "ColumnName": "Value", "ColumnType": "long" }],
                "Rows": [[42]]
              }]
            }
            """;
        using CapturingAdxHandler adxHandler = new(AdxResponse);
        using HttpClient adxClient = new(adxHandler);
        using TestWebApplicationFactory factory = new(adxClient);
        using HttpClient browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using KustoGatewayClient gatewayClient = new(browserClient);

        KustoQueryResult result = await gatewayClient.ExecuteAsync(new KustoQueryRequest(
            new Uri("https://help.kusto.windows.net"),
            "Samples",
            "print Value=42"));

        Assert.Equal("42", Assert.Single(Assert.Single(result.Tables).Rows).Values[0]);
        Assert.Equal(new Uri("https://help.kusto.windows.net/v1/rest/query"), adxHandler.RequestUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "server-only-token"), adxHandler.Authorization);
        Assert.DoesNotContain(
            "server-only-token",
            JsonSerializer.Serialize(
                KustoGatewayMapper.ToGatewayQueryResponse(result),
                KustoGatewayJsonContext.Default.KustoGatewayQueryResponse),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies non-finite ADX visualization bounds do not break the gateway JSON response.
    /// </summary>
    /// <returns>A task that completes after the chart result is returned.</returns>
    [Fact]
    public async Task ExecuteAsyncOmitsNonFiniteVisualizationBounds()
    {
        const string AdxResponse = """
                        {
                            "Tables": [
                                {
                                    "TableName": "PrimaryResult",
                                    "Columns": [
                                        { "ColumnName": "State", "ColumnType": "string" },
                                        { "ColumnName": "Events", "ColumnType": "long" }
                                    ],
                                    "Rows": [["TEXAS", 42]]
                                },
                                {
                                    "TableName": "@ExtendedProperties",
                                    "Columns": [
                                        { "ColumnName": "TableId", "ColumnType": "int" },
                                        { "ColumnName": "Key", "ColumnType": "string" },
                                        { "ColumnName": "Value", "ColumnType": "string" }
                                    ],
                                    "Rows": [[0, "Visualization", "{\"Visualization\":\"columnchart\",\"Title\":\"Top states\",\"YMin\":\"NaN\",\"YMax\":\"Infinity\"}"]]
                                }
                            ]
                        }
                        """;
        const string Query = """
                        StormEvents
                        | where StartTime > ago(7d)
                        | summarize Events = count() by State
                        | top 10 by Events
                        | render columnchart with (title = "Top states")
                        """;
        using CapturingAdxHandler adxHandler = new(AdxResponse);
        using HttpClient adxClient = new(adxHandler);
        using TestWebApplicationFactory factory = new(adxClient);
        using HttpClient browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using KustoGatewayClient gatewayClient = new(browserClient);
        KustoQueryRequest request = new(
                new Uri("https://help.kusto.windows.net"),
                "Samples",
                Query);

        KustoQueryResult result = await gatewayClient.ExecuteAsync(request);

        Assert.Equal("42", Assert.Single(Assert.Single(result.Tables).Rows).Values[1]);
        Assert.NotNull(result.Visualization);
        Assert.Equal(KustoVisualizationKind.ColumnChart, result.Visualization.Kind);
        Assert.Equal("Top states", result.Visualization.Title);
        Assert.Null(result.Visualization.YMinimum);
        Assert.Null(result.Visualization.YMaximum);
    }

    /// <summary>
    /// Verifies Browser management commands reach the shared management endpoint through the protected gateway.
    /// </summary>
    /// <returns>A task that completes after the returned command result is inspected.</returns>
    [Fact]
    public async Task ExecuteAsyncRoutesManagementCommandThroughProtectedGateway()
    {
        const string AdxResponse = """
            {
              "Tables": [{
                "TableName": "PrimaryResult",
                "Columns": [{ "ColumnName": "TableName", "ColumnType": "string" }],
                "Rows": [["SyntheticEvents"]]
              }]
            }
            """;
        using CapturingAdxHandler adxHandler = new(AdxResponse);
        using HttpClient adxClient = new(adxHandler);
        using TestWebApplicationFactory factory = new(adxClient);
        using HttpClient browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using KustoGatewayClient gatewayClient = new(browserClient);

        KustoQueryResult result = await gatewayClient.ExecuteAsync(new KustoQueryRequest(
            new Uri("https://help.kusto.windows.net"),
            "Samples",
            ".show tables"));

        Assert.Equal("SyntheticEvents", Assert.Single(Assert.Single(result.Tables).Rows).Values[0]);
        Assert.Equal(new Uri("https://help.kusto.windows.net/v1/rest/mgmt"), adxHandler.RequestUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "server-only-token"), adxHandler.Authorization);
    }

    /// <summary>
    /// Verifies graph rows stream through the protected gateway in bounded batches.
    /// </summary>
    /// <returns>A task that completes after graph tables and their summary are inspected.</returns>
    [Fact]
    public async Task ExecuteGraphAsyncStreamsAuthenticatedGraphBatches()
    {
        const string AdxResponse = """
            {
              "Tables": [
                {
                  "TableName": "__oke_graph_export_nodes",
                  "Columns": [
                    { "ColumnName": "__oke_graph_export_node_hash", "ColumnType": "long" },
                    { "ColumnName": "id", "ColumnType": "string" }
                  ],
                  "Rows": [[1, "alice"], [2, "server-1"]]
                },
                {
                  "TableName": "__oke_graph_export_edges",
                  "Columns": [
                    { "ColumnName": "__oke_graph_export_source_hash", "ColumnType": "long" },
                    { "ColumnName": "__oke_graph_export_target_hash", "ColumnType": "long" },
                    { "ColumnName": "relationship", "ColumnType": "string" }
                  ],
                  "Rows": [[1, 2, "AuthenticatedTo"]]
                }
              ]
            }
            """;
        using CapturingAdxHandler adxHandler = new(AdxResponse);
        using HttpClient adxClient = new(adxHandler);
        using TestWebApplicationFactory factory = new(adxClient);
        using HttpClient browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using KustoGatewayClient gatewayClient = new(browserClient);
        const string QueryText = "graph(\"SecurityGraph\")";
        KustoGraphQueryPlan plan = new KustoLanguageService()
            .GetGraphQueryPlanAtPosition(QueryText, QueryText.Length)!;
        CollectingGraphSink sink = new();

        KustoGraphExportSummary summary = await gatewayClient.ExecuteGraphAsync(
            new KustoQueryRequest(
                new Uri("https://help.kusto.windows.net"),
                "Samples",
                QueryText),
            plan,
            sink);

        Assert.Equal(2, summary.NodeCount);
        Assert.Equal(1, summary.EdgeCount);
        Assert.Equal(["alice", "server-1"], sink.NodeRows.Select(row => row[1]));
        Assert.Equal("AuthenticatedTo", Assert.Single(sink.EdgeRows)[2]);
        Assert.True(sink.NodesCompleted);
        Assert.True(sink.EdgesCompleted);
        Assert.Equal(new Uri("https://help.kusto.windows.net/v1/rest/query"), adxHandler.RequestUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "server-only-token"), adxHandler.Authorization);
    }

    /// <summary>
    /// Verifies Copilot model discovery and bounded turns traverse the authenticated gateway.
    /// </summary>
    /// <returns>A task that completes after the provider receives the mapped request.</returns>
    [Fact]
    public async Task CopilotUsesAuthenticatedAntiforgeryProtectedGateway()
    {
        using HttpClient adxClient = new(new CapturingAdxHandler());
        StubWebCopilotService copilotService = new();
        using TestWebApplicationFactory factory = new(adxClient, copilotService);
        using HttpClient browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using KustoGatewayClient gatewayClient = new(browserClient);
        KustoGatewayCopilotModelsResponse models = await gatewayClient.GetCopilotModelsAsync();
        GraphSnapshot localSnapshot = new(Guid.NewGuid(), Guid.NewGuid());
        KustoCopilotContext context = new(
            Guid.NewGuid(),
            KustoCopilotScopeKind.Graph,
            "Investigation",
            "MATCH (n) RETURN n",
            "2 nodes",
            "User, Host",
            string.Empty,
            localSnapshot);
        KustoGatewayCopilotTurn history = new()
        {
            UserPrompt = "Earlier bounded prompt",
            AssistantResponse = "{\"message\":\"Earlier answer\",\"query\":null,\"cypher\":null}",
        };

        KustoCopilotReply reply = await gatewayClient.SendCopilotAsync(
            context,
            "Find a route",
            "synthetic-deployment",
            "Consented graph snapshot",
            [history]);

        Assert.Equal(KustoAIProviderKind.AzureOpenAI, (KustoAIProviderKind)models.ProviderKind);
        Assert.Equal("synthetic-deployment", Assert.Single(models.Models).Id);
        Assert.Equal("Synthetic answer", reply.Message);
        Assert.Equal("MATCH (n) RETURN n", reply.ProposedCypher);
        Assert.Equal("Consented graph snapshot", copilotService.Context!.SharedDataText);
        Assert.Null(copilotService.Context.GraphSnapshot);
        Assert.Equal("Earlier bounded prompt", Assert.Single(copilotService.History!).UserPrompt);
    }

    /// <summary>
    /// Verifies an unconfigured Web host reports Copilot as unavailable without contacting Azure.
    /// </summary>
    /// <returns>A task that completes after model discovery is rejected.</returns>
    [Fact]
    public async Task CopilotModelsReportSafeConfigurationError()
    {
        using HttpClient adxClient = new(new CapturingAdxHandler());
        using TestWebApplicationFactory factory = new(adxClient);
        using HttpClient browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using KustoGatewayClient gatewayClient = new(browserClient);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => gatewayClient.GetCopilotModelsAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("Copilot:AzureOpenAIEndpoint", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultAzureCredential", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Copilot POSTs without the session antiforgery token never reach the provider.
    /// </summary>
    /// <returns>A task that completes after the gateway rejects the request.</returns>
    [Fact]
    public async Task CopilotWithoutAntiforgeryTokenIsRejected()
    {
        using HttpClient adxClient = new(new CapturingAdxHandler());
        StubWebCopilotService copilotService = new();
        using TestWebApplicationFactory factory = new(adxClient, copilotService);
        using HttpClient browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        KustoGatewayCopilotRequest request = new()
        {
            DocumentTitle = "Query 1",
            ModelId = "synthetic-deployment",
            OperationId = Guid.NewGuid(),
            QueryText = "print Value=42",
            Request = "Explain this query",
            ScopeKind = (int)KustoCopilotScopeKind.Query,
            Version = KustoGatewayRoutes.Version,
        };
        using JsonContent content = JsonContent.Create(
            request,
            KustoGatewayJsonContext.Default.KustoGatewayCopilotRequest);

        using HttpResponseMessage response = await browserClient.PostAsync(
            KustoGatewayRoutes.Copilot,
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(copilotService.Context);
    }

    /// <summary>
    /// Verifies the gateway mapper rejects browser-local history beyond its explicit turn bound.
    /// </summary>
    [Fact]
    public void CopilotRejectsUnboundedHistory()
    {
        KustoGatewayCopilotTurn[] history = Enumerable
            .Range(0, KustoGatewayRoutes.MaximumCopilotHistoryTurnCount + 1)
            .Select(index => new KustoGatewayCopilotTurn
            {
                AssistantResponse = $"Answer {index}",
                UserPrompt = $"Prompt {index}",
            })
            .ToArray();
        KustoGatewayCopilotRequest request = new()
        {
            DocumentTitle = "Query 1",
            History = history,
            ModelId = "synthetic-deployment",
            OperationId = Guid.NewGuid(),
            QueryText = "print Value=42",
            Request = "Explain this query",
            ScopeKind = (int)KustoCopilotScopeKind.Query,
            Version = KustoGatewayRoutes.Version,
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => KustoGatewayMapper.ToDomainCopilotContext(request));

        Assert.Contains("history exceeds", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies query POSTs without the session antiforgery token are rejected.
    /// </summary>
    /// <returns>A task that completes after the gateway response is inspected.</returns>
    [Fact]
    public async Task QueryWithoutAntiforgeryTokenIsRejected()
    {
        using CapturingAdxHandler adxHandler = new("{}");
        using HttpClient adxClient = new(adxHandler);
        using TestWebApplicationFactory factory = new(adxClient);
        using HttpClient browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        KustoGatewayContracts.KustoGatewayQueryRequest request = KustoGatewayMapper.ToGatewayRequest(
            Guid.NewGuid(),
            new KustoQueryRequest(
                new Uri("https://help.kusto.windows.net"),
                "Samples",
                "print Value=42"));
        using JsonContent content = JsonContent.Create(
            request,
            KustoGatewayJsonContext.Default.KustoGatewayQueryRequest);

        using HttpResponseMessage response = await browserClient.PostAsync(
            KustoGatewayRoutes.Query,
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(adxHandler.RequestUri);
    }

    private sealed class CapturingAdxHandler : HttpMessageHandler
    {
        private readonly Queue<string> responses;

        internal CapturingAdxHandler(params string[] responses)
        {
            this.responses = new Queue<string>(responses);
        }

        internal AuthenticationHeaderValue? Authorization { get; private set; }

        internal Uri? RequestUri { get; private set; }

        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Authorization = request.Headers.Authorization;
            RequestUri = request.RequestUri;
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class CollectingGraphSink : IKustoGraphExportSink
    {
        internal List<IReadOnlyList<string>> EdgeRows { get; } = [];

        internal bool EdgesCompleted { get; private set; }

        internal List<IReadOnlyList<string>> NodeRows { get; } = [];

        internal bool NodesCompleted { get; private set; }

        public ValueTask WriteBatchAsync(
            KustoGraphExportBatch batch,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<IReadOnlyList<string>> rows = batch.Kind == KustoGraphExportTableKind.Nodes
                ? NodeRows
                : EdgeRows;
            rows.AddRange(batch.Rows);

            if (batch.EndsTable && batch.Kind == KustoGraphExportTableKind.Nodes)
            {
                NodesCompleted = true;
            }
            else if (batch.EndsTable)
            {
                EdgesCompleted = true;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticTokenProvider : IKustoAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(
            Uri clusterUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("server-only-token");
        }
    }

    private sealed class StubWebCopilotService : IWebCopilotService
    {
        internal KustoCopilotContext? Context { get; private set; }

        internal IReadOnlyList<KustoGatewayCopilotTurn>? History { get; private set; }

        public KustoCopilotModel GetModel()
        {
            return new KustoCopilotModel("synthetic-deployment", "Synthetic deployment");
        }

        public Task<KustoCopilotReply> SendAsync(
            KustoCopilotContext context,
            string request,
            string modelId,
            IReadOnlyList<KustoGatewayCopilotTurn> history,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("Find a route", request);
            Assert.Equal("synthetic-deployment", modelId);
            Context = context;
            History = history;
            return Task.FromResult(new KustoCopilotReply(
                "Synthetic answer",
                null,
                "MATCH (n) RETURN n"));
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        internal const string SchemeName = "Test";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Claim[] claims =
            [
                new Claim("oid", "11111111-1111-1111-1111-111111111111"),
                new Claim("tid", "22222222-2222-2222-2222-222222222222"),
                new Claim("name", "Test User"),
                new Claim("preferred_username", "test@example.com"),
            ];
            ClaimsIdentity identity = new(claims, SchemeName);
            ClaimsPrincipal principal = new(identity);
            AuthenticationTicket ticket = new(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly HttpClient adxClient;
        private readonly IWebCopilotService? copilotService;

        internal TestWebApplicationFactory(
            HttpClient adxClient,
            IWebCopilotService? copilotService = null)
        {
            this.adxClient = adxClient;
            this.copilotService = copilotService;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
                services.RemoveAll<KustoExecutionService>();
                services.AddScoped(_ => new KustoExecutionService(
                    adxClient,
                    new StaticTokenProvider(),
                    new HttpsKustoEndpointPolicy()));
                if (copilotService is not null)
                {
                    services.RemoveAll<IWebCopilotService>();
                    services.AddSingleton(copilotService);
                }
            });
        }
    }

    private sealed class FederatedWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("WebAuthentication:Mode", "FederatedManagedIdentity");
            builder.UseSetting("AzureAd:TenantId", "22222222-2222-2222-2222-222222222222");
            builder.UseSetting("AzureAd:ClientId", "33333333-3333-3333-3333-333333333333");
            builder.UseSetting(
                "AzureAd:ClientCredentials:0:SourceType",
                "SignedAssertionFromManagedIdentity");
            builder.UseSetting(
                "AzureAd:ClientCredentials:0:ManagedIdentityClientId",
                "44444444-4444-4444-4444-444444444444");
            builder.UseSetting(
                "AzureAd:ClientCredentials:0:TokenExchangeUrl",
                "api://AzureADTokenExchange/.default");
        }
    }
}
