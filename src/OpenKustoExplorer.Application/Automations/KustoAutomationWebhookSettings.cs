namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Describes one persisted automation webhook endpoint source.
/// </summary>
public sealed class KustoAutomationWebhookSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomationWebhookSettings"/> class.
    /// </summary>
    /// <param name="endpointSource">How the endpoint is resolved.</param>
    /// <param name="storedUrl">The optional stored HTTPS URL.</param>
    /// <param name="environmentVariableName">The optional environment-variable name.</param>
    public KustoAutomationWebhookSettings(
        KustoAutomationWebhookEndpointSource endpointSource,
        Uri? storedUrl = null,
        string? environmentVariableName = null)
    {
        if (!Enum.IsDefined(endpointSource))
        {
            throw new ArgumentOutOfRangeException(nameof(endpointSource));
        }

        string? normalizedVariableName = string.IsNullOrWhiteSpace(environmentVariableName)
            ? null
            : environmentVariableName.Trim();
        if (endpointSource == KustoAutomationWebhookEndpointSource.StoredUrl)
        {
            ValidateStoredUrl(storedUrl);

            if (normalizedVariableName is not null)
            {
                throw new ArgumentException(
                    "A stored webhook URL cannot also use an environment variable.",
                    nameof(environmentVariableName));
            }
        }
        else
        {
            if (storedUrl is not null)
            {
                throw new ArgumentException(
                    "An environment-backed webhook cannot also store a URL.",
                    nameof(storedUrl));
            }

            if (normalizedVariableName is null || normalizedVariableName.Contains('='))
            {
                throw new ArgumentException(
                    "Enter a valid webhook environment-variable name.",
                    nameof(environmentVariableName));
            }
        }

        EndpointSource = endpointSource;
        StoredUrl = storedUrl;
        EnvironmentVariableName = normalizedVariableName;
    }

    /// <summary>
    /// Gets how the endpoint is resolved.
    /// </summary>
    public KustoAutomationWebhookEndpointSource EndpointSource { get; }

    /// <summary>
    /// Gets the stored HTTPS URL when <see cref="EndpointSource"/> is
    /// <see cref="KustoAutomationWebhookEndpointSource.StoredUrl"/>.
    /// </summary>
    public Uri? StoredUrl { get; }

    /// <summary>
    /// Gets the environment-variable name when <see cref="EndpointSource"/> is
    /// <see cref="KustoAutomationWebhookEndpointSource.EnvironmentVariable"/>.
    /// </summary>
    public string? EnvironmentVariableName { get; }

    private static void ValidateStoredUrl(Uri? storedUrl)
    {
        if (storedUrl is null
            || !storedUrl.IsAbsoluteUri
            || storedUrl.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(storedUrl.UserInfo))
        {
            throw new ArgumentException(
                "A stored webhook endpoint must be an absolute HTTPS URL without user information.",
                nameof(storedUrl));
        }
    }
}
