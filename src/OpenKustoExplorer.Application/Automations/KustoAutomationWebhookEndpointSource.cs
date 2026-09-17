namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Identifies how an automation webhook endpoint is resolved.
/// </summary>
public enum KustoAutomationWebhookEndpointSource
{
    /// <summary>
    /// Uses an HTTPS URL stored with the automation.
    /// </summary>
    StoredUrl,

    /// <summary>
    /// Resolves the HTTPS URL from an environment variable at delivery time.
    /// </summary>
    EnvironmentVariable,
}
