namespace OpenKustoExplorer.Web.Assistance;

/// <summary>
/// Contains non-secret Azure OpenAI settings for the Web host.
/// </summary>
internal sealed class WebCopilotOptions
{
    /// <summary>Gets the configuration section name.</summary>
    internal const string SectionName = "Copilot";

    /// <summary>Gets or sets the Azure OpenAI resource endpoint.</summary>
    public string AzureOpenAIEndpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the Azure OpenAI deployment name.</summary>
    public string Deployment { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional user-assigned managed identity client identifier.</summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum completion token count.</summary>
    public int MaximumOutputTokens { get; set; } = 4096;
}
