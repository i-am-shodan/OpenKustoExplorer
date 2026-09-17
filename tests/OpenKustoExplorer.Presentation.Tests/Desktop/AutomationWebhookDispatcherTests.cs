using System.Net;
using System.Text.Json;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Desktop;

namespace OpenKustoExplorer.Presentation.Tests.Desktop;

/// <summary>
/// Verifies automation webhook payloads, endpoint resolution, and delivery policy.
/// </summary>
public sealed class AutomationWebhookDispatcherTests
{
    /// <summary>
    /// Verifies a successful request contains only the versioned metadata contract.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DispatchPostsMetadataOnlyJson()
    {
        RecordingHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using HttpClient client = new(handler);
        using AutomationWebhookDispatcher dispatcher = new(
            client,
            delayAsync: static (_, _) => Task.CompletedTask);
        KustoAutomationNotification notification = CreateNotification(CreateStoredWebhook());

        string? failure = await dispatcher.DispatchAsync(notification, CancellationToken.None);

        Assert.Null(failure);
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://hooks.example.com/automation", request.Uri.AbsoluteUri.TrimEnd('/'));
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        using JsonDocument document = JsonDocument.Parse(request.Body);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("automation.notification", root.GetProperty("eventType").GetString());
        Assert.Equal(notification.RunId, root.GetProperty("eventId").GetGuid());
        Assert.Equal(notification.AutomationId, root.GetProperty("automation").GetProperty("id").GetGuid());
        Assert.Equal("Telemetry", root.GetProperty("automation").GetProperty("database").GetString());
        Assert.Equal("succeeded", root.GetProperty("run").GetProperty("status").GetString());
        Assert.Equal(6, root.GetProperty("result").GetProperty("rowCount").GetInt32());
        Assert.Equal(2, root.GetProperty("result").GetProperty("rowsChanged").GetInt32());
        Assert.Equal(2, root.GetProperty("triggers").GetArrayLength());
        Assert.DoesNotContain("Events | take 6", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Monitor: 6", request.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies transient responses use the two bounded retry delays.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DispatchRetriesTransientResponsesTwice()
    {
        HttpStatusCode[] statuses =
        [
            HttpStatusCode.InternalServerError,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.OK,
        ];
        RecordingHttpMessageHandler handler = new(
            attempt => new HttpResponseMessage(statuses[attempt - 1]));
        using HttpClient client = new(handler);
        List<TimeSpan> delays = [];
        using AutomationWebhookDispatcher dispatcher = new(
            client,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        string? failure = await dispatcher.DispatchAsync(
            CreateNotification(CreateStoredWebhook()),
            CancellationToken.None);

        Assert.Null(failure);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
        Assert.Single(handler.Requests.Select(request => request.Body).Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies every retryable HTTP status is attempted again.
    /// </summary>
    /// <param name="statusCode">The first response status.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(502)]
    public async Task DispatchRetriesEveryTransientHttpStatus(int statusCode)
    {
        RecordingHttpMessageHandler handler = new(
            attempt => new HttpResponseMessage(
                attempt == 1 ? (HttpStatusCode)statusCode : HttpStatusCode.OK));
        using HttpClient client = new(handler);
        List<TimeSpan> delays = [];
        using AutomationWebhookDispatcher dispatcher = new(
            client,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        string? failure = await dispatcher.DispatchAsync(
            CreateNotification(CreateStoredWebhook()),
            CancellationToken.None);

        Assert.Null(failure);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }

    /// <summary>
    /// Verifies transport exceptions are retried with a fresh request.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DispatchRetriesTransportFailure()
    {
        RecordingHttpMessageHandler handler = new(
            attempt => attempt == 1
                ? throw new HttpRequestException("Sensitive transport details")
                : new HttpResponseMessage(HttpStatusCode.OK));
        using HttpClient client = new(handler);
        using AutomationWebhookDispatcher dispatcher = new(
            client,
            delayAsync: static (_, _) => Task.CompletedTask);

        string? failure = await dispatcher.DispatchAsync(
            CreateNotification(CreateStoredWebhook()),
            CancellationToken.None);

        Assert.Null(failure);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies per-attempt cancellation is treated as a retryable timeout.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DispatchRetriesAttemptTimeoutTwice()
    {
        RecordingHttpMessageHandler handler = new(
            _ => throw new TaskCanceledException("Sensitive timeout details"));
        using HttpClient client = new(handler);
        using AutomationWebhookDispatcher dispatcher = new(
            client,
            delayAsync: static (_, _) => Task.CompletedTask);

        string? failure = await dispatcher.DispatchAsync(
            CreateNotification(CreateStoredWebhook()),
            CancellationToken.None);

        Assert.Equal("Webhook delivery timed out.", failure);
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain("Sensitive", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies permanent failures are not retried and redact the endpoint.
    /// </summary>
    /// <param name="statusCode">The terminal HTTP status.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(302)]
    [InlineData(400)]
    public async Task DispatchDoesNotRetryPermanentFailure(int statusCode)
    {
        RecordingHttpMessageHandler handler = new(
            _ => new HttpResponseMessage((HttpStatusCode)statusCode));
        using HttpClient client = new(handler);
        using AutomationWebhookDispatcher dispatcher = new(
            client,
            delayAsync: static (_, _) => Task.CompletedTask);

        string? failure = await dispatcher.DispatchAsync(
            CreateNotification(CreateStoredWebhook()),
            CancellationToken.None);

        Assert.Equal($"Webhook delivery returned HTTP status {statusCode}.", failure);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("hooks.example.com", failure, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies caller cancellation propagates without attempting delivery.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DispatchPropagatesCallerCancellation()
    {
        RecordingHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using HttpClient client = new(handler);
        using AutomationWebhookDispatcher dispatcher = new(client);
        using CancellationTokenSource cancellationSource = new();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(
                CreateNotification(CreateStoredWebhook()),
                cancellationSource.Token));
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Verifies environment-backed endpoints are trimmed and validated at delivery time.
    /// </summary>
    [Fact]
    public void ResolveEndpointReadsEnvironmentAtDeliveryTime()
    {
        string variableName = $"OPENKUSTOEXPLORER_WEBHOOK_TEST_{Guid.NewGuid():N}";
        KustoAutomationWebhookSettings settings = new(
            KustoAutomationWebhookEndpointSource.EnvironmentVariable,
            environmentVariableName: variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, "  https://hooks.example.com/from-environment  ");

            Uri endpoint = AutomationWebhookDispatcher.ResolveEndpoint(settings);

            Assert.Equal("https://hooks.example.com/from-environment", endpoint.AbsoluteUri.TrimEnd('/'));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    private static KustoAutomationWebhookSettings CreateStoredWebhook()
    {
        return new KustoAutomationWebhookSettings(
            KustoAutomationWebhookEndpointSource.StoredUrl,
            new Uri("https://hooks.example.com/automation"));
    }

    private static KustoAutomationNotification CreateNotification(
        KustoAutomationWebhookSettings webhook)
    {
        DateTimeOffset startedAtUtc = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
        KustoAutomationNotificationSettings settings = new(
            true,
            KustoAutomationRowCountComparison.GreaterThanOrEqual,
            5,
            false,
            false,
            null,
            null,
            null,
            587,
            true,
            "{name}: {row_count}",
            "{query}",
            webhook: webhook);
        KustoAutomation automation = new(
            Guid.Parse("f7615411-1504-44fb-9794-ed39baf89ce1"),
            "Monitor",
            new Uri("https://adx.example.com"),
            "Telemetry",
            "Events | take 6",
            TimeSpan.FromMinutes(5),
            startedAtUtc.AddHours(-1),
            startedAtUtc.AddMinutes(5),
            null,
            true,
            [],
            settings);
        KustoAutomationRun previousRun = CreateRun(
            Guid.Parse("59149c43-f1cd-4c2e-9909-a9b142df33dc"),
            startedAtUtc.AddMinutes(-5),
            4);
        KustoAutomationRun currentRun = CreateRun(
            Guid.Parse("33fc931a-6434-4c64-990c-4eab1777f1c7"),
            startedAtUtc,
            6);
        return Assert.IsType<KustoAutomationNotification>(
            KustoAutomationNotification.TryCreate(automation, currentRun, previousRun));
    }

    private static KustoAutomationRun CreateRun(Guid id, DateTimeOffset startedAtUtc, int rowCount)
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Value", "long")],
            Enumerable.Range(0, rowCount).Select(index => new KustoResultRow([$"{index}"])));
        return new KustoAutomationRun(
            id,
            startedAtUtc,
            startedAtUtc.AddSeconds(1),
            KustoAutomationRunStatus.Succeeded,
            null,
            new KustoQueryResult([table], TimeSpan.FromSeconds(1)));
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> responseFactory;

        public RecordingHttpMessageHandler(Func<int, HttpResponseMessage> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Content.Headers.ContentType!.ToString(),
                body));
            return responseFactory(Requests.Count);
        }
    }

    private sealed class RecordedRequest
    {
        public RecordedRequest(HttpMethod method, Uri uri, string contentType, string body)
        {
            Method = method;
            Uri = uri;
            ContentType = contentType;
            Body = body;
        }

        public HttpMethod Method { get; }

        public Uri Uri { get; }

        public string ContentType { get; }

        public string Body { get; }
    }
}
