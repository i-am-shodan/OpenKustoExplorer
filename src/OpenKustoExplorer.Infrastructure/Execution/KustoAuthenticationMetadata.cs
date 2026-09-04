using System.Text.Json;

namespace OpenKustoExplorer.Infrastructure.Execution;

/// <summary>
/// Contains the public-client authentication settings advertised by a Kusto cluster.
/// </summary>
internal sealed class KustoAuthenticationMetadata
{
    private KustoAuthenticationMetadata(
        string clientId,
        string redirectUri,
        string serviceResourceId,
        string authorityBaseUrl)
    {
        ClientId = clientId;
        RedirectUri = redirectUri;
        ServiceResourceId = serviceResourceId;
        AuthorityBaseUrl = authorityBaseUrl;
    }

    /// <summary>
    /// Gets the Kusto public-client application identifier.
    /// </summary>
    public string ClientId { get; }

    /// <summary>
    /// Gets the registered loopback redirect URI.
    /// </summary>
    public string RedirectUri { get; }

    /// <summary>
    /// Gets the Kusto service resource identifier used to construct an OAuth scope.
    /// </summary>
    public string ServiceResourceId { get; }

    /// <summary>
    /// Gets the Microsoft Entra login endpoint used with the organizations tenant.
    /// </summary>
    public string AuthorityBaseUrl { get; }

    /// <summary>
    /// Parses Kusto authentication metadata from the cluster response.
    /// </summary>
    /// <param name="json">The complete authentication metadata JSON.</param>
    /// <returns>The validated public-client settings.</returns>
    public static KustoAuthenticationMetadata Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement azureActiveDirectory = document.RootElement.GetProperty("AzureAD");
        string clientId = GetRequiredString(azureActiveDirectory, "KustoClientAppId");
        string redirectUri = GetRequiredString(azureActiveDirectory, "KustoClientRedirectUri");
        string serviceResourceId = GetRequiredString(azureActiveDirectory, "KustoServiceResourceId");
        string authorityBaseUrl = GetRequiredString(azureActiveDirectory, "LoginEndpoint");

        return new KustoAuthenticationMetadata(clientId, redirectUri, serviceResourceId, authorityBaseUrl);
    }

    private static string GetRequiredString(JsonElement parent, string propertyName)
    {
        string? value = parent.GetProperty(propertyName).GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Kusto authentication metadata is missing {propertyName}.");
        }

        return value;
    }
}
