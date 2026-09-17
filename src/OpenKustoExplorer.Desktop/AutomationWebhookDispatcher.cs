using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using OpenKustoExplorer.Application.Automations;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Delivers metadata-only automation notification events to HTTPS webhooks.
/// </summary>
internal sealed class AutomationWebhookDispatcher : IDisposable
{
    private static readonly TimeSpan DefaultAttemptTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    ];

    private readonly TimeSpan attemptTimeout;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Func<KustoAutomationWebhookSettings, Uri> endpointResolver;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutomationWebhookDispatcher"/> class.
    /// </summary>
    public AutomationWebhookDispatcher()
        : this(
            CreateHttpClient(),
            ResolveEndpoint,
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            DefaultAttemptTimeout,
            ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AutomationWebhookDispatcher"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP transport.</param>
    /// <param name="endpointResolver">The endpoint resolver.</param>
    /// <param name="delayAsync">The retry delay implementation.</param>
    /// <param name="attemptTimeout">The timeout applied to each HTTP attempt.</param>
    /// <param name="ownsHttpClient">Whether disposal also disposes the HTTP client.</param>
    internal AutomationWebhookDispatcher(
        HttpClient httpClient,
        Func<KustoAutomationWebhookSettings, Uri>? endpointResolver = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? attemptTimeout = null,
        bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        TimeSpan validatedTimeout = attemptTimeout ?? DefaultAttemptTimeout;
        if (validatedTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptTimeout));
        }

        this.httpClient = httpClient;
        this.endpointResolver = endpointResolver ?? ResolveEndpoint;
        this.delayAsync = delayAsync
            ?? (static (delay, cancellationToken) => Task.Delay(delay, cancellationToken));
        this.attemptTimeout = validatedTimeout;
        this.ownsHttpClient = ownsHttpClient;
    }

    /// <summary>
    /// Delivers one evaluated notification to its configured webhook.
    /// </summary>
    /// <param name="notification">The evaluated notification.</param>
    /// <param name="cancellationToken">Cancels delivery during application shutdown.</param>
    /// <returns>A redacted failure message, or <see langword="null"/> on success.</returns>
    public async Task<string?> DispatchAsync(
        KustoAutomationNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        KustoAutomationWebhookSettings webhook = notification.Settings.Webhook
            ?? throw new InvalidOperationException("The automation does not have a webhook configured.");
        Uri endpoint;

        try
        {
            endpoint = endpointResolver(webhook);
            ValidateResolvedEndpoint(endpoint);
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or InvalidOperationException)
        {
            return "The webhook endpoint is missing or invalid.";
        }

        byte[] payload = CreatePayload(notification);
        for (int attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            (bool Succeeded, bool Transient, string FailureMessage) result =
                await SendAttemptAsync(endpoint, payload, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return null;
            }

            if (!result.Transient || attempt == RetryDelays.Length)
            {
                return result.FailureMessage;
            }

            await delayAsync(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
        }

        return "Webhook delivery failed.";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    /// <summary>
    /// Resolves a stored or environment-backed endpoint without persisting the resolved value.
    /// </summary>
    /// <param name="settings">The persisted webhook settings.</param>
    /// <returns>The resolved HTTPS endpoint.</returns>
    internal static Uri ResolveEndpoint(KustoAutomationWebhookSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.EndpointSource == KustoAutomationWebhookEndpointSource.StoredUrl)
        {
            return settings.StoredUrl!;
        }

        string? value = Environment.GetEnvironmentVariable(settings.EnvironmentVariableName!);
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? endpoint))
        {
            throw new InvalidOperationException("The webhook environment variable is missing or invalid.");
        }

        ValidateResolvedEndpoint(endpoint);
        return endpoint;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = false,
        };
        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static HttpRequestMessage CreateRequest(Uri endpoint, byte[] payload)
    {
        HttpRequestMessage request = new(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8",
        };
        return request;
    }

    private static byte[] CreatePayload(KustoAutomationNotification notification)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("eventType", "automation.notification");
        writer.WriteString("eventId", notification.RunId);
        writer.WritePropertyName("automation");
        writer.WriteStartObject();
        writer.WriteString("id", notification.AutomationId);
        writer.WriteString("name", notification.AutomationName);
        writer.WriteString("clusterUri", notification.ClusterUri.AbsoluteUri);
        writer.WriteString("database", notification.DatabaseName);
        writer.WriteEndObject();
        writer.WritePropertyName("run");
        writer.WriteStartObject();
        writer.WriteString("id", notification.RunId);
        writer.WriteString("startedAtUtc", notification.StartedAtUtc);
        writer.WriteString("completedAtUtc", notification.CompletedAtUtc);
        writer.WriteString("status", GetRunStatusToken(notification.RunStatus));
        writer.WriteEndObject();
        writer.WritePropertyName("result");
        writer.WriteStartObject();
        writer.WriteNumber("rowCount", notification.RowCount);
        writer.WriteNumber("rowsChanged", notification.RowsChanged);
        writer.WriteEndObject();
        writer.WritePropertyName("triggers");
        writer.WriteStartArray();

        foreach (KustoAutomationNotificationTrigger trigger in notification.Triggers)
        {
            writer.WriteStartObject();
            if (trigger.Kind == KustoAutomationNotificationTriggerKind.RowCountChanged)
            {
                writer.WriteString("kind", "rowCountChanged");
            }
            else
            {
                writer.WriteString("kind", "rowCountComparison");
                writer.WriteString("operator", GetComparisonToken(trigger.Comparison!.Value));
                writer.WriteNumber("value", trigger.ComparisonValue!.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static string GetRunStatusToken(KustoAutomationRunStatus status)
    {
        return status switch
        {
            KustoAutomationRunStatus.Succeeded => "succeeded",
            KustoAutomationRunStatus.Failed => "failed",
            KustoAutomationRunStatus.Canceled => "canceled",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    private static string GetComparisonToken(KustoAutomationRowCountComparison comparison)
    {
        return comparison switch
        {
            KustoAutomationRowCountComparison.Equals => "equals",
            KustoAutomationRowCountComparison.GreaterThanOrEqual => "greaterThanOrEqual",
            KustoAutomationRowCountComparison.LessThanOrEqual => "lessThanOrEqual",
            KustoAutomationRowCountComparison.NotEquals => "notEquals",
            _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
        };
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        int numericStatusCode = (int)statusCode;
        return statusCode == HttpStatusCode.RequestTimeout
            || numericStatusCode == 429
            || numericStatusCode >= 500;
    }

    private static void ValidateResolvedEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException("The resolved webhook endpoint is invalid.");
        }
    }

    private async Task<(bool Succeeded, bool Transient, string FailureMessage)> SendAttemptAsync(
        Uri endpoint,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(endpoint, payload);
        using CancellationTokenSource attemptCancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptCancellationSource.CancelAfter(attemptTimeout);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                attemptCancellationSource.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? (true, false, string.Empty)
                : (
                    false,
                    IsTransient(response.StatusCode),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Webhook delivery returned HTTP status {(int)response.StatusCode}."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return (false, true, "Webhook delivery timed out.");
        }
        catch (HttpRequestException)
        {
            return (false, true, "Webhook delivery could not be completed.");
        }
    }
}
